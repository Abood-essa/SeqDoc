using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using RoslynProjectId = Microsoft.CodeAnalysis.ProjectId;
using StableDocumentId = SeqDoc.Core.Identity.DocumentId;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.ProgramIndex;

internal static class RoslynProgramIndexExtractor
{
    private const string ProducerVersion = "0.1.0-pass-b";

    /// <summary>
    /// Discriminator used in the path-free canonical metadata name of a file-local type. Roslyn's
    /// compiler metadata name for a file-local type embeds a syntax-tree path hash that changes with
    /// the checkout root, so Program Index identities must derive the type name from the source-level
    /// name/arity plus the stable declaring document id. This token marks that canonical form.
    /// </summary>
    private const string FileLocalDocumentMarker = "file-local:";

    private static readonly IReadOnlyDictionary<SyntaxTree, DocumentContext> EmptyDocuments =
        ImmutableDictionary<SyntaxTree, DocumentContext>.Empty;

    internal static readonly SymbolDisplayFormat IdentityFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.ExpandNullable);

    internal static readonly SymbolDisplayFormat SignatureFormat = IdentityFormat
        .WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints)
        .WithMemberOptions(
            SymbolDisplayMemberOptions.IncludeAccessibility
            | SymbolDisplayMemberOptions.IncludeModifiers
            | SymbolDisplayMemberOptions.IncludeContainingType
            | SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeType)
        .WithParameterOptions(
            SymbolDisplayParameterOptions.IncludeDefaultValue
            | SymbolDisplayParameterOptions.IncludeExtensionThis
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeOptionalBrackets
            | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeType);

    public static async Task<ProgramIndexSnapshot> ExtractAsync(
        LoadedCompilationProfile loaded,
        CompilationProfile profile,
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var documents = ImmutableArray.CreateBuilder<ProgramDocument>();
        var namespaces = ImmutableArray.CreateBuilder<ProgramNamespace>();
        var types = ImmutableArray.CreateBuilder<ProgramType>();
        var members = ImmutableArray.CreateBuilder<ProgramMember>();
        var methods = ImmutableArray.CreateBuilder<ProgramMethod>();
        var attributes = ImmutableArray.CreateBuilder<ProgramAttributeApplication>();
        var references = ImmutableArray.CreateBuilder<ProgramReference>();
        var invocations = ImmutableArray.CreateBuilder<ProgramInvocation>();
        var markers = ImmutableArray.CreateBuilder<ProgramInventoryMarker>();
        var projectDocuments = new Dictionary<StableProjectId, ImmutableArray<DocumentContext>>();
        var projectByRoslynId = loaded.Projects.ToDictionary(project => project.Project.Id);
        var projectByAssembly = new Dictionary<IAssemblySymbol, StableProjectId>(SymbolEqualityComparer.Default);
        foreach (var project in loaded.Projects)
        {
            projectByAssembly.TryAdd(project.Compilation.Assembly, project.StableId);
        }

        foreach (var loadedProject in loaded.Projects.OrderBy(project => project.StableId.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contexts = await ReadDocumentsAsync(loadedProject, repositoryRoot, cancellationToken).ConfigureAwait(false);
            projectDocuments.Add(loadedProject.StableId, contexts);
            documents.AddRange(contexts.Select(context => context.Document));

            var byTree = contexts.ToDictionary(context => context.Tree);
            ExtractSymbols(
                loadedProject,
                byTree,
                projectByAssembly,
                namespaces,
                types,
                members,
                methods,
                attributes,
                markers,
                cancellationToken);
            ExtractInvocations(loadedProject, byTree, projectByAssembly, invocations, cancellationToken);
            references.AddRange(ReadReferences(loadedProject, projectByRoslynId));
            references.AddRange(ReadPackages(loadedProject, profile));
        }

        var orderedDocuments = documents.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        var orderedReferences = references
            .DistinctBy(item => item.Id, StringComparer.Ordinal)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        var projects = loaded.Projects.Select(loadedProject =>
        {
            var projectReferences = loadedProject.Project.ProjectReferences
                .Where(reference => projectByRoslynId.ContainsKey(reference.ProjectId))
                .Select(reference => projectByRoslynId[reference.ProjectId].StableId)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToImmutableArray();
            var projectEvidence = ImmutableArray.Create(CreateConfigurationEvidence(
                loadedProject.RepositoryRelativePath,
                loadedProject.RepositoryRelativePath));
            var projectDocumentValues = projectDocuments[loadedProject.StableId]
                .OrderBy(item => item.Document.Id.Value, StringComparer.Ordinal)
                .SelectMany(item => new[] { item.Document.Id.Value, item.Document.ContentFingerprint });
            var buildFingerprint = Fingerprinting.Sequence(
                "project-build:v1",
                new[]
                {
                    profile.Id.Value,
                    loadedProject.RepositoryRelativePath,
                    loadedProject.Compilation.Assembly.Identity.ToString(),
                }.Concat(projectReferences.Select(id => id.Value)).Concat(projectDocumentValues));

            return new ProgramProject(
                loadedProject.StableId,
                loadedProject.Project.Name,
                loadedProject.RepositoryRelativePath,
                profile.Id,
                GetEffectiveTargetFramework(loadedProject, profile),
                ToProjectKind(loadedProject.Compilation.Options.OutputKind),
                buildFingerprint,
                projectReferences,
                projectEvidence);
        }).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();

        var inputManifestHash = Fingerprinting.Sequence(
            "input-manifest:v1",
            projects.Select(project => project.BuildFingerprint)
                .Concat(orderedDocuments.SelectMany(document => new[] { document.Id.Value, document.ContentFingerprint }))
                .Concat(orderedReferences.Select(reference => $"{reference.Kind}|{reference.Identity}|{reference.Version}")));
        var snapshot = new ProgramIndexSnapshot(
            1,
            ProducerVersion,
            profile,
            projects,
            orderedDocuments,
            namespaces.DistinctBy(item => item.Id).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            types.DistinctBy(item => item.Id).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            members.DistinctBy(item => item.Id).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            methods.DistinctBy(item => item.Id).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            attributes.DistinctBy(item => item.Id, StringComparer.Ordinal).OrderBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray(),
            orderedReferences,
            invocations.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            markers.DistinctBy(item => item.Id, StringComparer.Ordinal).OrderBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray(),
            loaded.Diagnostics,
            inputManifestHash,
            string.Empty);
        return snapshot with { IndexFingerprint = Fingerprinting.Index(snapshot) };
    }

    internal static async Task<ImmutableArray<DocumentContext>> ReadDocumentsAsync(
        LoadedProject loadedProject,
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var contexts = ImmutableArray.CreateBuilder<DocumentContext>();
        foreach (var document in loadedProject.Project.Documents.OrderBy(document => document.FilePath, StringComparer.Ordinal))
        {
            var context = await CreateDocumentContextAsync(
                loadedProject,
                document,
                repositoryRoot,
                sourceGenerated: false,
                cancellationToken).ConfigureAwait(false);
            if (context is not null)
            {
                contexts.Add(context);
            }
        }

        var generatedDocuments = await loadedProject.Project.GetSourceGeneratedDocumentsAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var document in generatedDocuments.OrderBy(document => document.Name, StringComparer.Ordinal))
        {
            var context = await CreateDocumentContextAsync(
                loadedProject,
                document,
                repositoryRoot,
                sourceGenerated: true,
                cancellationToken).ConfigureAwait(false);
            if (context is not null)
            {
                contexts.Add(context);
            }
        }

        return contexts
            .DistinctBy(context => context.Document.Id)
            .OrderBy(context => context.Document.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal static Dictionary<SyntaxTree, DocumentContext> CreateDocumentIndex(
        ImmutableArray<DocumentContext> contexts) =>
        contexts.ToDictionary(context => context.Tree);

    internal static string GetDocumentSortKey(
        SyntaxTree tree,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents) =>
        documents.TryGetValue(tree, out var document) ? document.Document.Id.Value : string.Empty;

    private static async Task<DocumentContext?> CreateDocumentContextAsync(
        LoadedProject loadedProject,
        Document document,
        string repositoryRoot,
        bool sourceGenerated,
        CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
        if (tree is null)
        {
            return null;
        }

        var projectDirectory = Path.GetDirectoryName(loadedProject.Project.FilePath!)!;
        var logicalProjectDirectory = Path.GetDirectoryName(loadedProject.RepositoryRelativePath) ?? string.Empty;
        var linkedLogicalPath = RepositoryRelativePath.Normalize(Path.Combine(
            logicalProjectDirectory,
            Path.Combine(document.Folders.Concat([document.Name]).ToArray())));
        var physicalPath = document.FilePath;
        var isIntermediateGenerated = physicalPath is not null
            && (IsUnder(Path.Combine(projectDirectory, "obj"), physicalPath)
                || IsKnownMsBuildGeneratedDocument(document.Name));

        DocumentIdentityKind identityKind;
        DocumentOrigin origin;
        string logicalPath;
        string? generatorIdentity = null;
        string? generatorHintName = null;
        if (sourceGenerated || isIntermediateGenerated)
        {
            origin = DocumentOrigin.GeneratedSource;
            identityKind = DocumentIdentityKind.GeneratedSource;
            generatorHintName = document is SourceGeneratedDocument generatedDocument
                ? generatedDocument.HintName
                : document.Name;
            generatorIdentity = sourceGenerated
                ? GetSourceGeneratorIdentity(document)
                : $"msbuild/{loadedProject.Project.Name}";
            logicalPath = $"generated/{generatorIdentity}/{generatorHintName}";
        }
        else
        {
            var isExternal = physicalPath is not null && !IsUnder(repositoryRoot, physicalPath);
            var physicalLogicalPath = physicalPath is null || isExternal
                ? linkedLogicalPath
                : ToRepositoryRelativePath(repositoryRoot, physicalPath);
            var linked = !string.Equals(physicalLogicalPath, linkedLogicalPath, StringComparison.Ordinal);
            origin = isExternal
                ? DocumentOrigin.ExternalSource
                : linked ? DocumentOrigin.LinkedSource : DocumentOrigin.Source;
            identityKind = isExternal
                ? DocumentIdentityKind.ExternalSource
                : linked ? DocumentIdentityKind.LinkedSource : DocumentIdentityKind.Source;
            logicalPath = linked || isExternal ? linkedLogicalPath : physicalLogicalPath;
        }

        var id = StableIdentity.CreateDocumentId(new DocumentIdentityDescriptor(
            loadedProject.StableId,
            identityKind,
            logicalPath,
            generatorIdentity,
            generatorHintName));
        var evidence = CreateSourceEvidence(
            id,
            logicalPath,
            text,
            new TextSpan(0, text.Length),
            null,
            origin == DocumentOrigin.GeneratedSource);
        var programDocument = new ProgramDocument(
            id,
            loadedProject.StableId,
            logicalPath,
            origin,
            Fingerprinting.Text(text.ToString()),
            null,
            [evidence]);
        return new DocumentContext(programDocument, tree, text);
    }

    private static void ExtractSymbols(
        LoadedProject project,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        ImmutableArray<ProgramNamespace>.Builder namespaces,
        ImmutableArray<ProgramType>.Builder types,
        ImmutableArray<ProgramMember>.Builder members,
        ImmutableArray<ProgramMethod>.Builder methods,
        ImmutableArray<ProgramAttributeApplication>.Builder attributes,
        ImmutableArray<ProgramInventoryMarker>.Builder markers,
        CancellationToken cancellationToken)
    {
        var projectEvidence = ImmutableArray.Create(CreateConfigurationEvidence(
            project.RepositoryRelativePath,
            project.Compilation.Assembly.Identity.ToString()));
        markers.Add(new ProgramInventoryMarker(
            $"marker:v1:{Fingerprinting.Sequence("binary", [project.StableId.Value, project.Compilation.Assembly.Identity.ToString()])}",
            project.StableId,
            InventoryMarkerKind.BinaryCandidate,
            null,
            projectEvidence));
        VisitNamespace(project.Compilation.Assembly.GlobalNamespace);

        var entryPoint = project.Compilation.GetEntryPoint(cancellationToken);
        if (entryPoint is not null)
        {
            var descriptor = CreateMethodDescriptor(entryPoint, ResolveProject(entryPoint, project.StableId, projectsByAssembly), documents);
            var symbol = StableIdentity.CreateSymbolId(descriptor);
            var evidence = CreateDeclarationEvidence(entryPoint, documents);
            markers.Add(new ProgramInventoryMarker(
                $"marker:v1:{Fingerprinting.Sequence("entry", [project.StableId.Value, symbol.Value])}",
                project.StableId,
                InventoryMarkerKind.EntryPointCandidate,
                symbol,
                evidence));
            markers.Add(new ProgramInventoryMarker(
                $"marker:v1:{Fingerprinting.Sequence("framework-configuration", [project.StableId.Value, symbol.Value])}",
                project.StableId,
                InventoryMarkerKind.FrameworkConfigurationCandidate,
                symbol,
                evidence));
        }

        void VisitNamespace(INamespaceSymbol namespaceSymbol)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceTypes = namespaceSymbol.GetTypeMembers().Where(IsSourceSymbol).ToArray();
            if (!namespaceSymbol.IsGlobalNamespace && (sourceTypes.Length > 0 || namespaceSymbol.GetNamespaceMembers().Any(HasSourceTypes)))
            {
                var namespaceId = CreateNamespaceId(namespaceSymbol, project);
                namespaces.Add(new ProgramNamespace(
                    namespaceId,
                    project.StableId,
                    namespaceSymbol.ToDisplayString(IdentityFormat),
                    CreateDeclarationEvidence(namespaceSymbol, documents)));
            }

            foreach (var type in sourceTypes.OrderBy(type => GetMetadataName(type, documents), StringComparer.Ordinal))
            {
                VisitType(type);
            }

            foreach (var child in namespaceSymbol.GetNamespaceMembers().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                VisitNamespace(child);
            }
        }

        bool HasSourceTypes(INamespaceSymbol symbol) =>
            symbol.GetTypeMembers().Any(IsSourceSymbol) || symbol.GetNamespaceMembers().Any(HasSourceTypes);

        void VisitType(INamedTypeSymbol type)
        {
            var typeId = CreateSymbolId(type, project.StableId, documents);
            var namespaceId = type.ContainingNamespace.IsGlobalNamespace
                ? CreateGlobalNamespaceId(project)
                : CreateNamespaceId(type.ContainingNamespace, project);
            if (type.ContainingNamespace.IsGlobalNamespace)
            {
                namespaces.Add(new ProgramNamespace(
                    namespaceId,
                    project.StableId,
                    string.Empty,
                    CreateDeclarationEvidence(type, documents)));
            }

            var evidence = CreateDeclarationEvidence(type, documents);
            SymbolId? baseType = type.BaseType is null || type.BaseType.SpecialType == SpecialType.System_Object
                ? null
                : CreateSymbolId(type.BaseType, ResolveProject(type.BaseType, project.StableId, projectsByAssembly), documents);
            var interfaceIds = type.Interfaces
                .Select(item => CreateSymbolId(item, ResolveProject(item, project.StableId, projectsByAssembly), documents))
                .Distinct()
                .OrderBy(item => item.Value, StringComparer.Ordinal)
                .ToImmutableArray();
            var signature = Fingerprinting.Sequence(
                "type-signature:v1",
                new[]
                {
                    type.ToDisplayString(SignatureFormat),
                    type.DeclaredAccessibility.ToString(),
                    type.IsStatic.ToString(CultureInfo.InvariantCulture),
                    type.IsAbstract.ToString(CultureInfo.InvariantCulture),
                    type.IsSealed.ToString(CultureInfo.InvariantCulture),
                    type.IsReadOnly.ToString(CultureInfo.InvariantCulture),
                    type.IsRefLikeType.ToString(CultureInfo.InvariantCulture),
                    type.EnumUnderlyingType?.ToDisplayString(IdentityFormat) ?? string.Empty,
                    type.DelegateInvokeMethod?.ToDisplayString(SignatureFormat) ?? string.Empty,
                    baseType?.Value ?? string.Empty,
                }.Concat(interfaceIds.Select(item => item.Value)));
            types.Add(new ProgramType(
                typeId,
                project.StableId,
                namespaceId,
                GetMetadataName(type, documents),
                ToTypeKind(type),
                baseType,
                interfaceIds,
                signature,
                evidence));
            AddAttributes(type, typeId, documents, attributes);
            if (type.TypeKind == TypeKind.Interface)
            {
                markers.Add(new ProgramInventoryMarker(
                    $"marker:v1:{Fingerprinting.Sequence("contract", [project.StableId.Value, typeId.Value])}",
                    project.StableId,
                    InventoryMarkerKind.ContractCandidate,
                    typeId,
                    evidence));
            }

            var addedMethods = new HashSet<MethodId>();
            foreach (var member in type.GetMembers().OrderBy(item => item.MetadataName, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (member)
                {
                    case IFieldSymbol field when IsSourceSymbol(field) && !field.IsImplicitlyDeclared:
                        AddMember(field, field.Type, ProgramMemberKind.Field);
                        break;
                    case IPropertySymbol property when IsSourceSymbol(property):
                        AddMember(property, property.Type, ProgramMemberKind.Property);
                        break;
                    case IEventSymbol @event when IsSourceSymbol(@event):
                        AddMember(@event, @event.Type, ProgramMemberKind.Event);
                        break;
                    case IMethodSymbol method when IsSourceSymbol(method)
                                                   && (!method.IsImplicitlyDeclared
                                                       || method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor):
                        AddMethod(method);
                        break;
                    case INamedTypeSymbol nested when IsSourceSymbol(nested):
                        VisitType(nested);
                        break;
                }
            }

            void AddMember(ISymbol member, ITypeSymbol memberType, ProgramMemberKind kind)
            {
                var memberId = CreateSymbolId(member, project.StableId, documents);
                var memberEvidence = CreateDeclarationEvidence(member, documents);
                var typeName = memberType.ToDisplayString(IdentityFormat);
                members.Add(new ProgramMember(
                    memberId,
                    project.StableId,
                    typeId,
                    kind,
                    member.Name,
                    typeName,
                    Fingerprinting.Sequence("member-signature:v1", [member.ToDisplayString(SignatureFormat), typeName]),
                    memberEvidence));
                AddAttributes(member, memberId, documents, attributes);
            }

            void AddMethod(IMethodSymbol method)
            {
                method = method.PartialDefinitionPart ?? method;
                var descriptor = CreateMethodDescriptor(method, project.StableId, documents);
                var methodId = StableIdentity.CreateMethodId(descriptor);
                if (!addedMethods.Add(methodId))
                {
                    return;
                }

                var symbolId = StableIdentity.CreateSymbolId(descriptor);
                var methodParts = ImmutableArray.CreateBuilder<IMethodSymbol>();
                methodParts.Add(method);
                if (method.PartialImplementationPart is { } implementation
                    && !SymbolEqualityComparer.Default.Equals(method, implementation))
                {
                    methodParts.Add(implementation);
                }
                var methodEvidence = methodParts
                    .SelectMany(part => CreateDeclarationEvidence(part, documents))
                    .DistinctBy(item => item.Id)
                    .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                    .ToImmutableArray();
                if (methodEvidence.IsEmpty && method.IsImplicitlyDeclared)
                {
                    methodEvidence = evidence;
                }

                var parameters = method.Parameters.Select(parameter => new ParameterDescriptor(
                    parameter.Name,
                    parameter.Type.ToDisplayString(IdentityFormat),
                    ToParameterRefKind(parameter.RefKind))).ToImmutableArray();
                var bodyTexts = methodParts.SelectMany(part => part.DeclaringSyntaxReferences)
                    .Select(reference => reference.GetSyntax(cancellationToken))
                    .OrderBy(syntax => GetDocumentSortKey(syntax.SyntaxTree, documents), StringComparer.Ordinal)
                    .ThenBy(syntax => syntax.SpanStart)
                    .Select(GetMethodBodyText)
                    .Where(value => value is not null)
                    .Select(value => value!)
                    .ToArray();
                var returnType = method.ReturnType.ToDisplayString(IdentityFormat);
                methods.Add(new ProgramMethod(
                    methodId,
                    symbolId,
                    typeId,
                    method.Name,
                    method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    parameters,
                    returnType,
                    Fingerprinting.Sequence("method-signature:v1", [method.ToDisplayString(SignatureFormat), returnType]),
                    bodyTexts.Length == 0 ? null : Fingerprinting.Sequence("method-body:v1", bodyTexts),
                    methodEvidence));
                AddAttributes(method, symbolId, documents, attributes);
                if (method.Name is "Configure" or "ConfigureServices")
                {
                    markers.Add(new ProgramInventoryMarker(
                        $"marker:v1:{Fingerprinting.Sequence("framework-configuration", [project.StableId.Value, symbolId.Value])}",
                        project.StableId,
                        InventoryMarkerKind.FrameworkConfigurationCandidate,
                        symbolId,
                        methodEvidence));
                }
            }
        }
    }

    private static void ExtractInvocations(
        LoadedProject project,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        ImmutableArray<ProgramInvocation>.Builder invocations,
        CancellationToken cancellationToken)
    {
        foreach (var document in documents.Values.OrderBy(item => item.Document.Id.Value, StringComparer.Ordinal))
        {
            var semanticModel = project.Compilation.GetSemanticModel(document.Tree);
            var root = document.Tree.GetRoot(cancellationToken);
            var siblingOrdinals = new Dictionary<(MethodId Method, string Kind), int>();
            var callSites = root.DescendantNodes()
                .Where(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
                .OrderBy(node => node.SpanStart);
            foreach (var syntax in callSites)
            {
                var (target, operationKind) = semanticModel.GetOperation(syntax, cancellationToken) switch
                {
                    IInvocationOperation invocation => (invocation.TargetMethod, "Invocation"),
                    IObjectCreationOperation creation when creation.Constructor is not null =>
                        (creation.Constructor, "ObjectCreation"),
                    _ => (null, string.Empty),
                };
                if (target is null)
                {
                    continue;
                }

                foreach (var initialContainingMethod in GetContainingMethods(
                             semanticModel.GetEnclosingSymbol(syntax.SpanStart, cancellationToken)))
                {
                    var containingMethod = initialContainingMethod;
                    while (containingMethod.MethodKind is MethodKind.AnonymousFunction or MethodKind.LocalFunction
                           && containingMethod.ContainingSymbol is IMethodSymbol containingMember)
                    {
                        containingMethod = containingMember;
                    }

                    if (string.IsNullOrWhiteSpace(containingMethod.MetadataName))
                    {
                        continue;
                    }

                    var containingProject = ResolveProject(containingMethod, project.StableId, projectsByAssembly);
                    var containingMethodId = StableIdentity.CreateMethodId(CreateMethodDescriptor(containingMethod, containingProject, documents));
                    var siblingKey = (containingMethodId, operationKind);
                    var siblingOrdinal = siblingOrdinals.GetValueOrDefault(siblingKey);
                    siblingOrdinals[siblingKey] = siblingOrdinal + 1;
                    var targetId = StableIdentity.CreateMethodId(CreateMethodDescriptor(
                        target,
                        ResolveProject(target, project.StableId, projectsByAssembly),
                        documents));
                    var operationId = StableIdentity.CreateOperationId(new OperationIdentityDescriptor(
                        document.Document.Id,
                        containingMethodId,
                        operationKind,
                        syntax.SpanStart,
                        syntax.Span.Length,
                        siblingOrdinal));
                    var evidence = CreateSourceEvidence(
                        document.Document.Id,
                        document.Document.LogicalPath,
                        document.Text,
                        syntax.Span,
                        target.ToDisplayString(IdentityFormat),
                        document.Document.Origin == DocumentOrigin.GeneratedSource);
                    var dispatchable = target.MethodKind == MethodKind.DelegateInvoke
                        || target.ContainingType.TypeKind == TypeKind.Interface
                        || target.IsAbstract
                        || (target.IsVirtual && !target.IsSealed && !target.ContainingType.IsSealed);
                    invocations.Add(new ProgramInvocation(
                        operationId,
                        containingMethodId,
                        targetId,
                        target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        [evidence],
                        dispatchable ? CertaintyLevel.Conservative : CertaintyLevel.Exact));
                }
            }
        }
    }

    private static ImmutableArray<IMethodSymbol> GetContainingMethods(ISymbol? enclosingSymbol)
    {
        return enclosingSymbol switch
        {
            IMethodSymbol method => [method],
            IFieldSymbol field when field.IsStatic => field.ContainingType.StaticConstructors,
            IFieldSymbol field => field.ContainingType.InstanceConstructors,
            IPropertySymbol property when property.IsStatic => property.ContainingType.StaticConstructors,
            IPropertySymbol property => property.ContainingType.InstanceConstructors,
            IEventSymbol @event when @event.IsStatic => @event.ContainingType.StaticConstructors,
            IEventSymbol @event => @event.ContainingType.InstanceConstructors,
            _ => [],
        };
    }

    private static IEnumerable<ProgramReference> ReadReferences(
        LoadedProject project,
        Dictionary<RoslynProjectId, LoadedProject> projectsById)
    {
        var evidence = ImmutableArray.Create(CreateConfigurationEvidence(
            project.RepositoryRelativePath,
            project.RepositoryRelativePath));
        foreach (var reference in project.Project.ProjectReferences.OrderBy(item => item.ProjectId.Id))
        {
            if (!projectsById.TryGetValue(reference.ProjectId, out var target))
            {
                continue;
            }

            var identity = target.RepositoryRelativePath;
            yield return new ProgramReference(
                $"reference:v1:{Fingerprinting.Sequence("project", [project.StableId.Value, identity])}",
                project.StableId,
                ProgramReferenceKind.Project,
                identity,
                null,
                evidence);
        }

        foreach (var reference in project.Compilation.References.OrderBy(item => item.Display, StringComparer.Ordinal))
        {
            if (project.Compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            var identity = assembly.Identity.Name;
            var version = assembly.Identity.Version.ToString();
            yield return new ProgramReference(
                $"reference:v1:{Fingerprinting.Sequence("assembly", [project.StableId.Value, identity, version])}",
                project.StableId,
                ProgramReferenceKind.Assembly,
                identity,
                version,
                evidence);
        }
    }

    private static IEnumerable<ProgramReference> ReadPackages(
        LoadedProject project,
        CompilationProfile profile)
    {
        var assetsPath = FindAssetsPath(project.Project);
        if (assetsPath is null)
        {
            yield break;
        }

        using var stream = File.OpenRead(assetsPath);
        using var assets = JsonDocument.Parse(stream);
        if (!assets.RootElement.TryGetProperty("project", out var projectElement)
            || !projectElement.TryGetProperty("frameworks", out var frameworks)
            || !assets.RootElement.TryGetProperty("targets", out var targets))
        {
            yield break;
        }

        var targetName = profile.RuntimeIdentifier is null
            ? profile.TargetFramework
            : $"{profile.TargetFramework}/{profile.RuntimeIdentifier}";
        var frameworkCandidates = frameworks.EnumerateObject().ToArray();
        var targetCandidates = targets.EnumerateObject().ToArray();
        var selectedFrameworks = frameworkCandidates
            .Where(item => string.Equals(item.Name, profile.TargetFramework, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var selectedTargets = targetCandidates
            .Where(item => string.Equals(item.Name, targetName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (selectedFrameworks.Length == 0 && frameworkCandidates.Length == 1)
        {
            selectedFrameworks = frameworkCandidates;
        }

        if (selectedTargets.Length == 0 && targetCandidates.Length == 1)
        {
            selectedTargets = targetCandidates;
        }

        if (selectedFrameworks.Length != 1 || selectedTargets.Length != 1)
        {
            yield break;
        }

        var selectedFramework = selectedFrameworks[0];
        var selectedTarget = selectedTargets[0];

        var resolved = selectedTarget.Value.EnumerateObject()
            .Select(item => item.Name.Split('/', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(parts => parts[1]).Order(StringComparer.Ordinal).First(),
                StringComparer.OrdinalIgnoreCase);
        var evidence = ImmutableArray.Create(CreateConfigurationEvidence(
            project.RepositoryRelativePath,
            project.RepositoryRelativePath));
        if (!selectedFramework.Value.TryGetProperty("dependencies", out var dependencies))
        {
            yield break;
        }

        foreach (var dependency in dependencies.EnumerateObject().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (dependency.Value.TryGetProperty("target", out var target)
                && !string.Equals(target.GetString(), "Package", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            resolved.TryGetValue(dependency.Name, out var version);
            yield return new ProgramReference(
                $"reference:v1:{Fingerprinting.Sequence("package", [project.StableId.Value, dependency.Name, version ?? string.Empty])}",
                project.StableId,
                ProgramReferenceKind.Package,
                dependency.Name,
                version,
                evidence);
        }
    }

    private static void AddAttributes(
        ISymbol symbol,
        SymbolId target,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents,
        ImmutableArray<ProgramAttributeApplication>.Builder attributes)
    {
        foreach (var attribute in symbol.GetAttributes().OrderBy(item => item.AttributeClass?.ToDisplayString(IdentityFormat), StringComparer.Ordinal))
        {
            var attributeType = attribute.AttributeClass?.ToDisplayString(IdentityFormat) ?? "<unknown>";
            var constructor = attribute.AttributeConstructor?.ToDisplayString(IdentityFormat) ?? "<unknown>";
            var arguments = attribute.ConstructorArguments
                .Select(argument => argument.ToCSharpString())
                .Concat(attribute.NamedArguments.Select(argument => $"{argument.Key}={argument.Value.ToCSharpString()}"))
                .ToImmutableArray();
            var evidence = attribute.ApplicationSyntaxReference is null
                ? ImmutableArray<EvidenceRef>.Empty
                : CreateSyntaxEvidence(attribute.ApplicationSyntaxReference, attributeType, documents);
            var anchor = attribute.ApplicationSyntaxReference is null
                ? string.Empty
                : Fingerprinting.Sequence("attribute-anchor:v1", [
                    GetDocumentSortKey(attribute.ApplicationSyntaxReference.SyntaxTree, documents),
                    attribute.ApplicationSyntaxReference.Span.Start.ToString(CultureInfo.InvariantCulture),
                    attribute.ApplicationSyntaxReference.Span.Length.ToString(CultureInfo.InvariantCulture),
                ]);
            var id = $"attribute:v1:{Fingerprinting.Sequence("attribute", new[] { target.Value, attributeType, constructor, anchor }.Concat(arguments))}";
            attributes.Add(new ProgramAttributeApplication(id, target, attributeType, constructor, arguments, evidence));
        }
    }

    internal static ImmutableArray<EvidenceRef> CreateDeclarationEvidence(
        ISymbol symbol,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents) =>
        symbol.DeclaringSyntaxReferences
            .OrderBy(reference => GetDocumentSortKey(reference.SyntaxTree, documents), StringComparer.Ordinal)
            .ThenBy(reference => reference.Span.Start)
            .SelectMany(reference => CreateSyntaxEvidence(
                reference,
                symbol.ToDisplayString(IdentityFormat),
                documents))
            .ToImmutableArray();

    private static ImmutableArray<EvidenceRef> CreateSyntaxEvidence(
        SyntaxReference reference,
        string symbol,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents)
    {
        if (!documents.TryGetValue(reference.SyntaxTree, out var document))
        {
            return [];
        }

        return [CreateSourceEvidence(
            document.Document.Id,
            document.Document.LogicalPath,
            document.Text,
            reference.Span,
            symbol,
            document.Document.Origin == DocumentOrigin.GeneratedSource)];
    }

    internal static EvidenceRef CreateSourceEvidence(
        StableDocumentId document,
        string artifact,
        SourceText text,
        TextSpan span,
        string? symbol,
        bool generated)
    {
        var lineSpan = text.Lines.GetLinePositionSpan(span);
        var range = new SourceRange(
            document,
            new SourcePosition(lineSpan.Start.Line, lineSpan.Start.Character),
            new SourcePosition(lineSpan.End.Line, lineSpan.End.Character));
        var kind = generated ? EvidenceKind.GeneratedSource : EvidenceKind.Source;
        var id = StableIdentity.CreateEvidenceId(new EvidenceIdentityDescriptor(
            kind,
            artifact,
            document,
            span.Start,
            span.Length,
            symbol,
            CertaintyLevel.Exact));
        return new EvidenceRef(id, kind, artifact, range, symbol, null, CertaintyLevel.Exact);
    }

    internal static EvidenceRef CreateConfigurationEvidence(string artifact, string detail)
    {
        var id = StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(
            EvidenceKind.Configuration,
            artifact,
            null,
            null,
            null,
            null,
            CertaintyLevel.Exact,
            Detail: detail));
        return new EvidenceRef(id, EvidenceKind.Configuration, artifact, null, null, detail, CertaintyLevel.Exact);
    }

    private static SymbolId CreateNamespaceId(INamespaceSymbol symbol, LoadedProject project)
    {
        var containingName = symbol.ContainingNamespace?.IsGlobalNamespace == false
            ? symbol.ContainingNamespace.ToDisplayString(IdentityFormat)
            : string.Empty;
        return StableIdentity.CreateSymbolId(new SymbolIdentityDescriptor(
            project.StableId,
            project.Compilation.Assembly.Identity.ToString(),
            containingName,
            SymbolIdentityKind.Namespace,
            symbol.Name,
            0,
            null,
            [],
            null));
    }

    private static SymbolId CreateGlobalNamespaceId(LoadedProject project) =>
        StableIdentity.CreateSymbolId(new SymbolIdentityDescriptor(
            project.StableId,
            project.Compilation.Assembly.Identity.ToString(),
            string.Empty,
            SymbolIdentityKind.Namespace,
            "<global>",
            0,
            null,
            [],
            null));

    internal static SymbolId CreateSymbolId(ISymbol symbol, StableProjectId project) =>
        StableIdentity.CreateSymbolId(CreateSymbolDescriptor(symbol, project));

    internal static SymbolId CreateSymbolId(
        ISymbol symbol,
        StableProjectId project,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents) =>
        StableIdentity.CreateSymbolId(CreateSymbolDescriptor(symbol, project, documents));

    internal static SymbolIdentityDescriptor CreateSymbolDescriptor(ISymbol symbol, StableProjectId project) =>
        CreateSymbolDescriptor(symbol, project, EmptyDocuments);

    internal static SymbolIdentityDescriptor CreateSymbolDescriptor(
        ISymbol symbol,
        StableProjectId project,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents)
    {
        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        var containingName = symbol is INamedTypeSymbol namedType
            ? namedType.ContainingType is null
                ? namedType.ContainingNamespace.ToDisplayString(IdentityFormat)
                : GetMetadataName(namedType.ContainingType, documents)
            : type is null ? string.Empty : GetMetadataName(type, documents);
        var memberType = symbol switch
        {
            IFieldSymbol field => field.Type.ToDisplayString(IdentityFormat),
            IPropertySymbol property => property.Type.ToDisplayString(IdentityFormat),
            IEventSymbol @event => @event.Type.ToDisplayString(IdentityFormat),
            _ => null,
        };
        var explicitInterface = symbol switch
        {
            IPropertySymbol property => property.ExplicitInterfaceImplementations
                .OrderBy(item => item.ToDisplayString(IdentityFormat), StringComparer.Ordinal)
                .FirstOrDefault()?.ToDisplayString(IdentityFormat),
            IEventSymbol @event => @event.ExplicitInterfaceImplementations
                .OrderBy(item => item.ToDisplayString(IdentityFormat), StringComparer.Ordinal)
                .FirstOrDefault()?.ToDisplayString(IdentityFormat),
            _ => null,
        };
        var parameters = symbol is IPropertySymbol indexer
            ? indexer.Parameters.Select(parameter => new ParameterIdentityDescriptor(
                ToParameterRefKind(parameter.RefKind),
                parameter.Type.ToDisplayString(IdentityFormat))).ToImmutableArray()
            : ImmutableArray<ParameterIdentityDescriptor>.Empty;
        return new SymbolIdentityDescriptor(
            project,
            symbol.ContainingAssembly.Identity.ToString(),
            containingName,
            symbol switch
            {
                INamedTypeSymbol => SymbolIdentityKind.NamedType,
                IFieldSymbol => SymbolIdentityKind.Field,
                IPropertySymbol => SymbolIdentityKind.Property,
                IEventSymbol => SymbolIdentityKind.Event,
                IMethodSymbol => SymbolIdentityKind.Method,
                _ => throw new ArgumentOutOfRangeException(nameof(symbol)),
            },
            symbol is INamedTypeSymbol named
                ? GetCanonicalTypeMetadataName(named, documents)
                : symbol.MetadataName,
            symbol is INamedTypeSymbol genericType ? genericType.Arity : 0,
            explicitInterface,
            parameters,
            memberType);
    }

    internal static SymbolIdentityDescriptor CreateMethodDescriptor(IMethodSymbol method, StableProjectId project) =>
        CreateMethodDescriptor(method, project, EmptyDocuments);

    internal static SymbolIdentityDescriptor CreateMethodDescriptor(
        IMethodSymbol method,
        StableProjectId project,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents)
    {
        var explicitInterface = method.ExplicitInterfaceImplementations
            .OrderBy(item => item.ToDisplayString(IdentityFormat), StringComparer.Ordinal)
            .FirstOrDefault()
            ?.ToDisplayString(IdentityFormat);
        return new SymbolIdentityDescriptor(
            project,
            method.ContainingAssembly.Identity.ToString(),
            GetMetadataName(method.ContainingType, documents),
            SymbolIdentityKind.Method,
            method.MetadataName,
            method.Arity,
            explicitInterface,
            method.Parameters.Select(parameter => new ParameterIdentityDescriptor(
                ToParameterRefKind(parameter.RefKind),
                parameter.Type.ToDisplayString(IdentityFormat))).ToImmutableArray(),
            method.ReturnType.ToDisplayString(IdentityFormat),
            method.MethodKind == MethodKind.Conversion);
    }

    internal static StableProjectId ResolveProject(
        ISymbol symbol,
        StableProjectId fallback,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly) =>
        projectsByAssembly.GetValueOrDefault(symbol.ContainingAssembly, fallback);

    internal static string GetMetadataName(INamedTypeSymbol type) => GetMetadataName(type, EmptyDocuments);

    internal static string GetMetadataName(
        INamedTypeSymbol type,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents)
    {
        var names = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            names.Push(GetCanonicalTypeMetadataName(current, documents));
        }

        var namespaceName = type.ContainingNamespace.ToDisplayString(IdentityFormat);
        return string.IsNullOrEmpty(namespaceName)
            ? string.Join('+', names)
            : $"{namespaceName}.{string.Join('+', names)}";
    }

    /// <summary>
    /// Returns the path-free canonical metadata name token for one type. Non-file-local types keep
    /// the exact compiler metadata name byte-for-byte. A file-local type embeds a syntax-tree path
    /// hash in its compiler metadata name, so Program Index identities derive the token from the
    /// source-level name/arity plus the stable declaring document id from the syntax-tree document
    /// map; when no declaring document is resolvable (context-free callers) the original metadata
    /// name is preserved unchanged so external consumers keep byte-identical behavior.
    /// </summary>
    private static string GetCanonicalTypeMetadataName(
        INamedTypeSymbol type,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents)
    {
        if (!type.IsFileLocal)
        {
            return type.MetadataName;
        }

        var documentId = ResolveFileLocalDocumentId(type, documents);
        if (string.IsNullOrEmpty(documentId))
        {
            return type.MetadataName;
        }

        var aritySuffix = type.Arity > 0 ? "`" + type.Arity.ToString(CultureInfo.InvariantCulture) : string.Empty;
        return $"{type.Name}{aritySuffix}<{FileLocalDocumentMarker}{documentId}>";
    }

    private static string ResolveFileLocalDocumentId(
        INamedTypeSymbol type,
        IReadOnlyDictionary<SyntaxTree, DocumentContext> documents)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (documents.TryGetValue(reference.SyntaxTree, out var document))
            {
                return document.Document.Id.Value;
            }
        }

        return string.Empty;
    }

    private static string GetSourceGeneratorIdentity(Document document)
    {
        if (document.Folders.Count > 0)
        {
            return string.Join('/', document.Folders);
        }

        if (document.FilePath is not null)
        {
            var normalized = document.FilePath.Replace('\\', '/');
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                // Roslyn suffixes generated paths with generator assembly, generator type, and hint name.
                return string.Join('/', parts.Skip(parts.Length - 3).Take(2));
            }
        }

        throw new InvalidOperationException(
            $"Roslyn did not expose a checkout-independent generator identity for hint '{document.Name}'.");
    }

    private static string? FindAssetsPath(Project project)
    {
        var projectDirectory = Path.GetDirectoryName(project.FilePath!)!;
        var defaultPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        foreach (var generatedDocument in project.Documents
                     .Where(document => document.FilePath is not null && IsKnownMsBuildGeneratedDocument(document.Name))
                     .OrderBy(document => document.Name, StringComparer.Ordinal))
        {
            var directory = Path.GetDirectoryName(generatedDocument.FilePath);
            for (var depth = 0; directory is not null && depth < 8; depth++)
            {
                var candidate = Path.Combine(directory, "project.assets.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = Path.GetDirectoryName(directory);
            }
        }

        return null;
    }

    private static string GetEffectiveTargetFramework(
        LoadedProject project,
        CompilationProfile profile)
    {
        var assetsPath = FindAssetsPath(project.Project);
        if (assetsPath is null)
        {
            return profile.TargetFramework;
        }

        using var stream = File.OpenRead(assetsPath);
        using var assets = JsonDocument.Parse(stream);
        if (!assets.RootElement.TryGetProperty("project", out var projectElement)
            || !projectElement.TryGetProperty("frameworks", out var frameworks))
        {
            return profile.TargetFramework;
        }

        var available = frameworks.EnumerateObject().Select(item => item.Name).ToArray();
        return available.FirstOrDefault(item => string.Equals(
                   item,
                   profile.TargetFramework,
                   StringComparison.OrdinalIgnoreCase))
               ?? (available.Length == 1 ? available[0] : profile.TargetFramework);
    }

    private static string? GetMethodBodyText(SyntaxNode syntax) => syntax switch
    {
        BaseMethodDeclarationSyntax method => method.Body?.ToFullString()
            ?? method.ExpressionBody?.ToFullString(),
        AccessorDeclarationSyntax accessor => accessor.Body?.ToFullString()
            ?? accessor.ExpressionBody?.ToFullString(),
        ArrowExpressionClauseSyntax expression => expression.ToFullString(),
        _ => null,
    };

    private static string ToRepositoryRelativePath(string repositoryRoot, string path)
    {
        var relative = Path.GetRelativePath(repositoryRoot, path);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Source document '{path}' is outside the selected repository root.");
        }

        return RepositoryRelativePath.Normalize(relative);
    }

    private static bool IsUnder(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative)
            && !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsKnownMsBuildGeneratedDocument(string name) =>
        name.EndsWith(".AssemblyInfo.cs", StringComparison.Ordinal)
        || name.EndsWith(".GlobalUsings.g.cs", StringComparison.Ordinal)
        || name.EndsWith(".AssemblyAttributes.cs", StringComparison.Ordinal);

    private static bool IsSourceSymbol(ISymbol symbol) => symbol.Locations.Any(location => location.IsInSource);

    private static ProjectKind ToProjectKind(OutputKind kind) => kind switch
    {
        OutputKind.ConsoleApplication or OutputKind.WindowsApplication or OutputKind.WindowsRuntimeApplication =>
            ProjectKind.Executable,
        OutputKind.DynamicallyLinkedLibrary or OutputKind.NetModule => ProjectKind.Library,
        _ => ProjectKind.Unknown,
    };

    private static ProgramTypeKind ToTypeKind(INamedTypeSymbol type) => (type.TypeKind, type.IsRecord) switch
    {
        (TypeKind.Class, true) => ProgramTypeKind.RecordClass,
        (TypeKind.Struct, true) => ProgramTypeKind.RecordStruct,
        (TypeKind.Class, false) => ProgramTypeKind.Class,
        (TypeKind.Struct, false) => ProgramTypeKind.Struct,
        (TypeKind.Interface, _) => ProgramTypeKind.Interface,
        (TypeKind.Enum, _) => ProgramTypeKind.Enum,
        (TypeKind.Delegate, _) => ProgramTypeKind.Delegate,
        _ => ProgramTypeKind.Unknown,
    };

    internal static ParameterRefKind ToParameterRefKind(RefKind kind) => kind switch
    {
        RefKind.Ref => ParameterRefKind.Ref,
        RefKind.Out => ParameterRefKind.Out,
        RefKind.In => ParameterRefKind.In,
        RefKind.RefReadOnlyParameter => ParameterRefKind.RefReadOnly,
        _ => ParameterRefKind.None,
    };

    internal sealed record DocumentContext(ProgramDocument Document, SyntaxTree Tree, SourceText Text);
}
