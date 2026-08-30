using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SeqDoc.Analysis.Roslyn.Frameworks;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Analysis.Roslyn.Semantics;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Behavior;

/// <summary>Extracts Roslyn-neutral behavior input and semantic companion facts from one loaded compilation.</summary>
internal static class RoslynBehaviorExtractor
{
    private const string ProducerVersion = "0.1.0-pass-b";

    public static async Task<ProfileAnalysisArtifacts> ExtractAsync(
        LoadedCompilationProfile loaded,
        CompilationProfile profile,
        string programIndexFingerprint,
        string repositoryRoot,
        ImmutableArray<string> repositoryOwnedConfigurationFiles,
        CancellationToken cancellationToken)
    {
        var bodies = ImmutableArray.CreateBuilder<ExtractedMethodBody>();
        var instantiations = ImmutableArray.CreateBuilder<TypeInstantiationFact>();
        var interfaceImplementations = ImmutableArray.CreateBuilder<InterfaceImplementationFact>();
        var methodOverrides = ImmutableArray.CreateBuilder<MethodOverrideFact>();
        var extractionDiagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        var typeNodes = ImmutableArray.CreateBuilder<ExtractedTypeNode>();
        var semanticFacts = new RoslynSemanticFactCollector();
        var frameworkRequest = new FrameworkAnalysisRequestCollector();
        var dependencyInjectionFacts = new RoslynDependencyInjectionFactCollector();
        var structuralResultFacts = new RoslynStructuralResultFactCollector();
        var nonGetFacts = new RoslynNonGetSemanticFactCollector();
        var configurationFacts = new RoslynConfigurationSemanticFactCollector();
        var conditionalDependencyInjectionFacts = new RoslynConditionalDependencyInjectionFactCollector();
        var callbackBoundaryFacts = new RoslynCallbackBoundaryFactCollector();
        callbackBoundaryFacts.SetIdentityFactories(
            (operation, methodId, kind, blockOrdinal, evaluationOrdinal, siblingOrdinal) =>
                CreateOperationId(operation, methodId, kind, blockOrdinal, evaluationOrdinal, siblingOrdinal, callbackBoundaryFacts.Documents),
            (operation, methodEvidence) => ResolveEvidence(operation, callbackBoundaryFacts.Documents, methodEvidence));
        var predicateFacts = new RoslynPredicateFactCollector();
        var projectByAssembly = new Dictionary<IAssemblySymbol, StableProjectId>(SymbolEqualityComparer.Default);
        foreach (var project in loaded.Projects)
        {
            projectByAssembly.TryAdd(project.Compilation.Assembly, project.StableId);
        }

        // Pass 1: preload every loaded project's documents and semantic models in one deterministic
        // project order and register all contexts with the callback collector BEFORE any method body
        // is extracted. Cross-project callback contracts and method-group targets then resolve for
        // every caller regardless of which project is processed first (regression).
        var projectContexts = new Dictionary<string, LoadedProjectContext>(StringComparer.Ordinal);
        foreach (var loadedProject in loaded.Projects.OrderBy(project => project.StableId.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contexts = await RoslynProgramIndexExtractor.ReadDocumentsAsync(
                loadedProject,
                repositoryRoot,
                cancellationToken).ConfigureAwait(false);
            var byTree = RoslynProgramIndexExtractor.CreateDocumentIndex(contexts);
            var modelByTree = byTree.ToDictionary(
                pair => pair.Key,
                pair => loadedProject.Compilation.GetSemanticModel(pair.Key));
            callbackBoundaryFacts.AddProjectContext(byTree, modelByTree, projectByAssembly);
            projectContexts.Add(loadedProject.StableId.Value, new LoadedProjectContext(byTree, modelByTree));
        }

        // Pass 2: perform the accepted extraction and companion collection using the cached
        // per-project documents/models. Per-project processing order never affects callback
        // resolution because every document/model context was already registered in pass 1.
        foreach (var loadedProject in loaded.Projects.OrderBy(project => project.StableId.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (byTree, modelByTree) = projectContexts[loadedProject.StableId.Value];

            dependencyInjectionFacts.SetAuthoritativeSymbols(
                loadedProject.StableId,
                loadedProject.Compilation.GetTypeByMetadataName(RoslynDependencyInjectionFactCollector.ServiceCollectionInterfaceMetadataName),
                loadedProject.Compilation.GetTypeByMetadataName(RoslynDependencyInjectionFactCollector.ServiceCollectionServiceExtensionsMetadataName));
            structuralResultFacts.SetAuthoritativeSymbols(
                loadedProject.StableId,
                loadedProject.Compilation.GetTypeByMetadataName(RoslynStructuralResultFactCollector.ControllerBaseMetadataName));
            nonGetFacts.SetAuthoritativeSymbols(
                loadedProject.StableId,
                loadedProject.Compilation.GetTypeByMetadataName(RoslynNonGetSemanticFactCollector.ControllerBaseMetadataName));
            configurationFacts.SetAuthoritativeSymbols(
                loadedProject.StableId,
                loadedProject.Compilation.GetTypeByMetadataName(RoslynConfigurationSemanticFactCollector.ConfigurationBinderMetadataName),
                loadedProject.Compilation.GetTypeByMetadataName(RoslynConfigurationSemanticFactCollector.IConfigurationMetadataName),
                loadedProject.Compilation.GetTypeByMetadataName(RoslynConfigurationSemanticFactCollector.WebApplicationMetadataName));
            conditionalDependencyInjectionFacts.SetAuthoritativeSymbols(
                loadedProject.StableId,
                loadedProject.Compilation.GetTypeByMetadataName(RoslynDependencyInjectionFactCollector.ServiceCollectionInterfaceMetadataName),
                loadedProject.Compilation.GetTypeByMetadataName(RoslynDependencyInjectionFactCollector.ServiceCollectionServiceExtensionsMetadataName));
            var hostChainProof = CoreWcfHostChainScanner.Scan(loadedProject.Compilation, byTree);
            frameworkRequest.SetHostChainProof(hostChainProof);
            callbackBoundaryFacts.AddHostChainProof(hostChainProof);

            CollectTypeHierarchy(loadedProject, byTree, projectByAssembly, typeNodes, cancellationToken);
            CollectInterfaceImplementations(loadedProject, byTree, projectByAssembly, interfaceImplementations, cancellationToken);
            CollectMethodOverrides(loadedProject, byTree, projectByAssembly, methodOverrides, cancellationToken);
            ExtractMethodBodies(
                loadedProject,
                byTree,
                modelByTree,
                projectByAssembly,
                bodies,
                instantiations,
                extractionDiagnostics,
                semanticFacts,
                frameworkRequest,
                dependencyInjectionFacts,
                structuralResultFacts,
                nonGetFacts,
                configurationFacts,
                callbackBoundaryFacts,
                predicateFacts,
                profile.Id,
                 cancellationToken);
            CollectTopLevelStatementRegistrations(
                loadedProject,
                byTree,
                modelByTree,
                projectByAssembly,
                profile.Id,
                dependencyInjectionFacts,
                configurationFacts,
                conditionalDependencyInjectionFacts,
                frameworkRequest,
                cancellationToken);
        }

        var orderedMethods = bodies
            .DistinctBy(body => body.Method)
            .OrderBy(body => body.Method.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var orderedTypes = typeNodes
            .DistinctBy(node => node.Id)
            .OrderBy(node => node.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var orderedInstantiations = instantiations
            .DistinctBy(fact => (fact.InstantiatedType, fact.CreatingOperation))
            .OrderBy(fact => fact.InstantiatedType.Value, StringComparer.Ordinal)
            .ThenBy(fact => fact.CreatingOperation.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        var orderedImplementations = interfaceImplementations
            .DistinctBy(fact => (fact.Implementation, fact.InterfaceMember))
            .OrderBy(fact => fact.Implementation.Value, StringComparer.Ordinal)
            .ThenBy(fact => fact.InterfaceMember.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var orderedOverrides = methodOverrides
            .DistinctBy(fact => (fact.Override, fact.BaseMethod))
            .OrderBy(fact => fact.Override.Value, StringComparer.Ordinal)
            .ThenBy(fact => fact.BaseMethod.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        var input = new ExtractedBehaviorInput(
            profile,
            programIndexFingerprint,
            orderedMethods,
            new ExtractedTypeHierarchy(orderedTypes, IsComplete: true),
            orderedInstantiations,
            orderedImplementations,
            orderedOverrides,
            loaded.Diagnostics.AddRange(extractionDiagnostics),
            string.Empty);
        var inputWithFingerprint = input with { InputFingerprint = BehaviorFingerprint.ComputeInput(input) };
        var diagnostics = loaded.Diagnostics.AddRange(extractionDiagnostics);
        var factSet = semanticFacts.Build(profile, programIndexFingerprint, diagnostics);
        var dependencyInjectionSet = dependencyInjectionFacts.Build(profile, programIndexFingerprint, diagnostics);
        var conditionalDependencyInjectionSet = conditionalDependencyInjectionFacts.Build(
            profile,
            programIndexFingerprint,
            diagnostics,
            dependencyInjectionSet.Registrations);
        var structuralResultSet = structuralResultFacts.Build(profile, programIndexFingerprint, diagnostics);
        var nonGetSet = nonGetFacts.Build(profile, programIndexFingerprint, diagnostics);
        var configurationSet = await configurationFacts.BuildAsync(
            profile,
            programIndexFingerprint,
            diagnostics,
            repositoryRoot,
            repositoryOwnedConfigurationFiles,
            loaded.Projects,
            cancellationToken).ConfigureAwait(false);
        var callbackBoundarySet = callbackBoundaryFacts.Build(profile, programIndexFingerprint, diagnostics, cancellationToken);
        var predicateSet = predicateFacts.Build(profile, programIndexFingerprint, diagnostics);

        // accepted contract companion projection: exact anonymous/local callback target bodies have no accepted
        // extracted Method Flow, so their source-backed descendant invocations become companion
        // framework operation descriptors (sharing the boundary member operation ids). They join the
        // existing framework-model request before BuildOperations so models and the scenario graph
        // see callback-contained work without any Behavior IR, Method Flow, or fingerprint change.
        foreach (var callbackOperation in callbackBoundaryFacts.BuildFrameworkOperations())
        {
            frameworkRequest.AddOperation(callbackOperation);
        }

        var frameworkOperations = frameworkRequest.BuildOperations();
        var minimalApiHandlerFacts = RoslynMinimalApiHandlerFactCollector.Collect(
            profile,
            programIndexFingerprint,
            projectContexts.Values.SelectMany(context => context.Models.Values.Select(model => (model, context.Documents))),
            frameworkOperations,
            cancellationToken);

        return new ProfileAnalysisArtifacts(
            inputWithFingerprint,
            factSet,
            frameworkOperations,
            frameworkRequest.BuildSymbols(),
            dependencyInjectionSet,
            structuralResultSet,
            nonGetSet,
            configurationSet,
            conditionalDependencyInjectionSet,
            callbackBoundarySet,
            predicateSet,
            minimalApiHandlerFacts);
    }

    private static void CollectMethodOverrides(
        LoadedProject project,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        ImmutableArray<MethodOverrideFact>.Builder facts,
        CancellationToken cancellationToken)
    {
        foreach (var type in EnumerateSourceTypes(project.Compilation, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var member in type.GetMembers())
            {
                if (member is not IMethodSymbol method || method.IsImplicitlyDeclared)
                {
                    continue;
                }

                var overridden = method.OverriddenMethod;
                while (overridden is not null)
                {
                    if (projectsByAssembly.ContainsKey(overridden.ContainingAssembly))
                    {
                        var overrideId = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
                            method.PartialDefinitionPart ?? method,
                            ResolveProject(method, project.StableId, projectsByAssembly)));
                        var baseId = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
                            overridden.PartialDefinitionPart ?? overridden,
                            ResolveProject(overridden, project.StableId, projectsByAssembly)));
                        facts.Add(new MethodOverrideFact(
                            overrideId,
                            baseId,
                            CreateMethodEvidence(method, documents),
                            CertaintyLevel.Exact));
                    }

                    overridden = overridden.OverriddenMethod;
                }
            }
        }
    }

    private static void CollectInterfaceImplementations(
        LoadedProject project,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        ImmutableArray<InterfaceImplementationFact>.Builder facts,
        CancellationToken cancellationToken)
    {
        var sourceTypes = EnumerateSourceTypes(project.Compilation, cancellationToken).ToArray();
        foreach (var type in sourceTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var iface in type.AllInterfaces)
            {
                foreach (var member in iface.GetMembers())
                {
                    if (member is not IMethodSymbol interfaceMethod)
                    {
                        continue;
                    }

                    var implementation = type.FindImplementationForInterfaceMember(interfaceMethod);
                    if (implementation is not IMethodSymbol implementationMethod)
                    {
                        continue;
                    }

                    foreach (var candidate in EnumerateImplementations(implementationMethod, sourceTypes))
                    {
                        if (candidate.IsAbstract || !projectsByAssembly.ContainsKey(candidate.ContainingAssembly))
                        {
                            continue;
                        }

                        var candidateId = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
                            candidate.PartialDefinitionPart ?? candidate,
                            ResolveProject(candidate, project.StableId, projectsByAssembly)));
                        var interfaceId = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
                            interfaceMethod.PartialDefinitionPart ?? interfaceMethod,
                            ResolveProject(interfaceMethod, project.StableId, projectsByAssembly)));
                        facts.Add(new InterfaceImplementationFact(
                            candidateId,
                            interfaceId,
                            CreateMethodEvidence(candidate, documents),
                            CertaintyLevel.Exact));
                    }
                }
            }
        }
    }

    private static IEnumerable<IMethodSymbol> EnumerateImplementations(
        IMethodSymbol implementation,
        INamedTypeSymbol[] sourceTypes)
    {
        yield return implementation;
        foreach (var type in sourceTypes)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IMethodSymbol method && OverridesTo(method, implementation))
                {
                    yield return method;
                }
            }
        }
    }

    private static bool OverridesTo(IMethodSymbol method, IMethodSymbol target)
    {
        var current = method.OverriddenMethod;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, target))
            {
                return true;
            }

            current = current.OverriddenMethod;
        }

        return false;
    }

    private static void ExtractMethodBodies(
        LoadedProject project,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        ImmutableArray<ExtractedMethodBody>.Builder bodies,
        ImmutableArray<TypeInstantiationFact>.Builder instantiations,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics,
        RoslynSemanticFactCollector semanticFacts,
        FrameworkAnalysisRequestCollector frameworkRequest,
        RoslynDependencyInjectionFactCollector dependencyInjectionFacts,
        RoslynStructuralResultFactCollector structuralResultFacts,
        RoslynNonGetSemanticFactCollector nonGetFacts,
        RoslynConfigurationSemanticFactCollector configurationFacts,
        RoslynCallbackBoundaryFactCollector callbackBoundaryFacts,
        RoslynPredicateFactCollector predicateFacts,
        CompilationProfileId profileId,
        CancellationToken cancellationToken)
    {
        foreach (var type in EnumerateSourceTypes(project.Compilation, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var member in type.GetMembers().OrderBy(member => member.MetadataName, StringComparer.Ordinal))
            {
                switch (member)
                {
                    case IMethodSymbol method when IsExtractedMethod(method):
                        ExtractOneBody(project, method, documents, models, projectsByAssembly, bodies, instantiations, diagnostics, semanticFacts, frameworkRequest, dependencyInjectionFacts, structuralResultFacts, nonGetFacts, configurationFacts, callbackBoundaryFacts, predicateFacts, profileId, cancellationToken);
                        ProjectMethodSymbol(method, project.StableId, documents, frameworkRequest);
                        // Constructors have no extractable behavior body (the accepted extractor
                        // reports them as bodyless), so DI constructor parameters are collected
                        // directly from the symbol, independent of body extraction, and only for
                        // exact admitted ASP.NET controllers using the same compiler-proven
                        // ApiController/ControllerBase boundary as the accepted C-1 model.
                        if (method.MethodKind == MethodKind.Constructor
                            && method.Parameters.Length > 0
                            && method.ContainingType is INamedTypeSymbol containingType
                            && AspNetCoreControllerBoundary.IsExactAdmittedController(containingType, project.Compilation))
                        {
                            var identityMethod = method.PartialDefinitionPart ?? method;
                            dependencyInjectionFacts.AddConstructorParameters(
                                identityMethod,
                                StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
                                    identityMethod,
                                    ResolveProject(identityMethod, project.StableId, projectsByAssembly))),
                                CreateMethodEvidence(identityMethod, documents));
                        }

                        break;
                    case IPropertySymbol property:
                        if (property.GetMethod is { } getter)
                        {
                            ExtractOneBody(project, getter, documents, models, projectsByAssembly, bodies, instantiations, diagnostics, semanticFacts, frameworkRequest, dependencyInjectionFacts, structuralResultFacts, nonGetFacts, configurationFacts, callbackBoundaryFacts, predicateFacts, profileId, cancellationToken);
                        }

                        if (property.SetMethod is { } setter)
                        {
                            ExtractOneBody(project, setter, documents, models, projectsByAssembly, bodies, instantiations, diagnostics, semanticFacts, frameworkRequest, dependencyInjectionFacts, structuralResultFacts, nonGetFacts, configurationFacts, callbackBoundaryFacts, predicateFacts, profileId, cancellationToken);
                        }

                        break;
                    case IEventSymbol @event:
                        if (@event.AddMethod is { } adder)
                        {
                            ExtractOneBody(project, adder, documents, models, projectsByAssembly, bodies, instantiations, diagnostics, semanticFacts, frameworkRequest, dependencyInjectionFacts, structuralResultFacts, nonGetFacts, configurationFacts, callbackBoundaryFacts, predicateFacts, profileId, cancellationToken);
                        }

                        if (@event.RemoveMethod is { } remover)
                        {
                            ExtractOneBody(project, remover, documents, models, projectsByAssembly, bodies, instantiations, diagnostics, semanticFacts, frameworkRequest, dependencyInjectionFacts, structuralResultFacts, nonGetFacts, configurationFacts, callbackBoundaryFacts, predicateFacts, profileId, cancellationToken);
                        }

                        break;
                }
            }
        }
    }

    /// <summary>
    /// Projects one source method symbol into the framework-model request with its controlled
    /// eligibility shape and source evidence. Non-controller methods are projected too; the accepted
    /// model decides applicability and rejects them without producing facts.
    /// </summary>
    private static void ProjectMethodSymbol(
        IMethodSymbol method,
        StableProjectId project,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        FrameworkAnalysisRequestCollector frameworkRequest)
    {
        var identityMethod = method.PartialDefinitionPart ?? method;
        var evidence = CreateMethodEvidence(identityMethod, documents);
        var descriptor = FrameworkAnalysisRequestProjector.ProjectMethodSymbol(identityMethod, project, evidence, documents);
        if (descriptor is not null)
        {
            frameworkRequest.AddSymbol(descriptor);
        }
    }

    /// <summary>
    /// Companion-only traversal for top-level statements. The accepted behavior extractor treats the
    /// synthesized <c>&lt;Main&gt;$</c> method as bodyless and never admits implicit methods, so
    /// Program.cs registrations, exact configuration reads, exact direct-local condition associations,
    /// the exact <c>WebApplication.CreateBuilder</c> provider-precedence observation, and the accepted contract
    /// conditional DI arm facts are projected here through a separate Roslyn traversal that reuses
    /// stable method identities and repository-relative source evidence without touching accepted
    /// behavior output or fingerprints. Configuration condition facts are projected here too because
    /// the synthesized top-level method has no CFG; the direct-local shape is the same single-write
    /// local assignment accepted contract admits, and extracted methods are never traversed here so Method Flow
    /// remains their sole control authority.
    /// </summary>
    private static void CollectTopLevelStatementRegistrations(
        LoadedProject project,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        Dictionary<SyntaxTree, SemanticModel> models,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        CompilationProfileId profileId,
        RoslynDependencyInjectionFactCollector dependencyInjectionFacts,
        RoslynConfigurationSemanticFactCollector configurationFacts,
        RoslynConditionalDependencyInjectionFactCollector conditionalDependencyInjectionFacts,
        FrameworkAnalysisRequestCollector frameworkRequest,
        CancellationToken cancellationToken)
    {
        var compilation = project.Compilation;
        var topLevelInitializers = CollectTopLevelInitializers(compilation, models, cancellationToken);

        // Pass 1: every top-level invocation of the synthesized <Main>$ method projects its plain DI
        // registration, admitted configuration read, and provider-precedence fact. Reads are collected
        // at compilation scope because the read declaration and the <c>if</c> that consumes its local
        // are separate top-level global statements (and may live in separate files) yet all belong to
        // the same synthesized method.
        var readDrafts = new List<TopLevelReadDraft>();
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (!models.TryGetValue(syntaxTree, out var model))
            {
                continue;
            }

            foreach (var globalStatement in syntaxTree
                         .GetRoot(cancellationToken)
                         .DescendantNodes()
                         .OfType<GlobalStatementSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var topLevelMethod = ResolveTopLevelMethod(
                    compilation,
                    model,
                    globalStatement,
                    project,
                    documents,
                    projectsByAssembly,
                    cancellationToken);
                if (topLevelMethod is null)
                {
                    continue;
                }

                var (methodId, methodEvidence) = topLevelMethod.Value;
                var operationById = new Dictionary<IOperation, OperationId>(ReferenceEqualityComparer.Instance);
                foreach (var invocationSyntax in globalStatement.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (model.GetOperation(invocationSyntax, cancellationToken) is not IInvocationOperation invocation)
                    {
                        continue;
                    }

                    if (invocation.Syntax is null || invocation.Syntax.Span.Length <= 0)
                    {
                        continue;
                    }

                    var operationId = CreateOperationId(
                        invocation,
                        methodId,
                        "Invocation",
                        blockOrdinal: 0,
                        evaluationOrdinal: 0,
                        siblingOrdinal: 0,
                        documents);
                    var invocationEvidence = ResolveEvidence(invocation, documents, methodEvidence);
                    operationById.TryAdd(invocation, operationId);
                    frameworkRequest.AddOperation(FrameworkAnalysisRequestProjector.ProjectOperationDescriptor(
                        invocation,
                        methodId,
                        operationId,
                        invocationEvidence,
                        operationById,
                        documents,
                        models,
                        project.StableId,
                        profile: profileId,
                        localInitializers: topLevelInitializers,
                        hostChainProof: frameworkRequest.HostChainProof,
                        dispatchCancellationToken: cancellationToken));
                    dependencyInjectionFacts.AddRegistration(
                        project.StableId,
                        methodId,
                        invocation,
                        operationId,
                        invocationEvidence);
                    if (configurationFacts.TryAdmitRead(
                            project.StableId,
                            methodId,
                            invocation,
                            operationId,
                            out var key,
                            out _,
                            invocationEvidence))
                    {
                        readDrafts.Add(new TopLevelReadDraft(
                            invocation,
                            operationId,
                            key,
                            TryResolveAssignedLocal(invocationSyntax, model, out var assignedLocal)
                                ? assignedLocal
                                : null));
                    }

                    if (configurationFacts.TryAdmitProviderPrecedence(project.StableId, invocation))
                    {
                        configurationFacts.AddProviderPrecedence(
                            methodId,
                            operationId,
                            invocationEvidence);
                    }
                }
            }
        }

        static IReadOnlyDictionary<ILocalSymbol, IOperation> CollectTopLevelInitializers(
            Compilation compilation,
            IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
            CancellationToken cancellationToken)
        {
            var candidates = new Dictionary<ILocalSymbol, IOperation>(SymbolEqualityComparer.Default);
            var duplicate = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            var reassigned = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (!models.TryGetValue(tree, out var model))
                {
                    continue;
                }
                foreach (var statement in tree.GetRoot(cancellationToken).DescendantNodes().OfType<GlobalStatementSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var operation = model.GetOperation(statement.Statement, cancellationToken);
                    foreach (var declarator in statement.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (declarator.Initializer?.Value is not { } value
                            || model.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol local
                            || local.DeclaringSyntaxReferences.Length != 1
                            || model.GetOperation(value, cancellationToken) is not { } initializer)
                        {
                            continue;
                        }
                        if (!candidates.TryAdd(local, initializer)) { candidates.Remove(local); duplicate.Add(local); }
                    }
                    if (operation is null)
                    {
                        continue;
                    }
                    foreach (var reference in operation.DescendantsAndSelf().OfType<ILocalReferenceOperation>())
                    {
                        for (IOperation? current = reference; current?.Parent is { } parent; current = parent)
                        {
                            if (parent is IAssignmentOperation assignment && assignment.Target.DescendantsAndSelf().Contains(reference)
                                || parent is IIncrementOrDecrementOperation increment && increment.Target.DescendantsAndSelf().Contains(reference)
                                || parent is IArgumentOperation argument && argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
                            {
                                reassigned.Add(reference.Local);
                                break;
                            }
                        }
                    }
                }
            }
            var result = new Dictionary<ILocalSymbol, IOperation>(SymbolEqualityComparer.Default);
            foreach (var pair in candidates)
            {
                if (!duplicate.Contains(pair.Key) && !reassigned.Contains(pair.Key))
                {
                    result.Add(pair.Key, pair.Value);
                }
            }

            return result;
        }

        // Pass 2: every top-level <c>if</c> statement whose condition is the single-write, unescaped
        // direct local of an admitted compilation-scope read projects the accepted contract condition fact and the
        // accepted contract conditional DI arm facts for the exact registrations directly enclosed by its arms.
        // The local usage summary is computed once across the whole top-level body so a reassignment,
        // compound/increment write, ref/out escape, or multiple condition consumption anywhere fails
        // the direct-local association closed (regression).
        var localUsage = CollectTopLevelLocalUsage(compilation, models, cancellationToken);
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (!models.TryGetValue(syntaxTree, out var model))
            {
                continue;
            }

            foreach (var globalStatement in syntaxTree
                         .GetRoot(cancellationToken)
                         .DescendantNodes()
                         .OfType<GlobalStatementSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var topLevelMethod = ResolveTopLevelMethod(
                    compilation,
                    model,
                    globalStatement,
                    project,
                    documents,
                    projectsByAssembly,
                    cancellationToken);
                if (topLevelMethod is null)
                {
                    continue;
                }

                var (methodId, methodEvidence) = topLevelMethod.Value;
                CollectTopLevelConditionalFacts(
                    project,
                    globalStatement,
                    model,
                    documents,
                    methodId,
                    methodEvidence,
                    readDrafts,
                    localUsage,
                    configurationFacts,
                    conditionalDependencyInjectionFacts,
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// Builds one compiler-bound local usage summary across every top-level statement of the
    /// synthesized <c>&lt;Main&gt;$</c> method. Writes count the declaration initializer, simple
    /// assignments, compound assignments, and increment/decrement targets; a by-reference escape is
    /// any ref/out/in argument carrying the local; and an if-condition consumption is the local
    /// flowing directly (through implicit conversions) into an actual <c>if</c> statement condition.
    /// Nested function bodies never participate because Method Flow remains their sole control
    /// authority (regression).
    /// </summary>
    private static Dictionary<ILocalSymbol, LocalUsageSummary> CollectTopLevelLocalUsage(
        Compilation compilation,
        Dictionary<SyntaxTree, SemanticModel> models,
        CancellationToken cancellationToken)
    {
        var usage = new Dictionary<ILocalSymbol, LocalUsageSummary>(SymbolEqualityComparer.Default);
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (!models.TryGetValue(syntaxTree, out var model))
            {
                continue;
            }

            foreach (var globalStatement in syntaxTree
                         .GetRoot(cancellationToken)
                         .DescendantNodes()
                         .OfType<GlobalStatementSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Roslyn's operation tree has no operation anchored at the GlobalStatementSyntax
                // wrapper: the usable operation anchor is the contained statement itself. Anchoring
                // at the wrapper yields no root and leaves the whole synthesized top-level usage
                // summary empty, which would fail every legitimate single-write local closed (regression).
                if (model.GetOperation(globalStatement.Statement, cancellationToken) is not { } root)
                {
                    continue;
                }

                foreach (var operation in EnumerateTopLevelOperations(root))
                {
                    switch (operation)
                    {
                        case IVariableDeclaratorOperation declarator
                            when declarator.Initializer is not null
                            && declarator.Symbol is ILocalSymbol declaredLocal:
                            usage[declaredLocal] = IncrementWrite(usage, declaredLocal);
                            break;
                        case ILocalReferenceOperation reference:
                            UpdateLocalUsage(usage, reference);
                            break;
                    }
                }
            }
        }

        return usage;
    }

    private static LocalUsageSummary IncrementWrite(
        Dictionary<ILocalSymbol, LocalUsageSummary> usage,
        ILocalSymbol local)
    {
        var current = usage.GetValueOrDefault(local, new LocalUsageSummary(0, false, 0));
        return current with { WriteCount = current.WriteCount + 1 };
    }

    /// <summary>
    /// Classifies one local reference by its parent operation: a write target, a by-reference
    /// argument escape, or an actual <c>if</c> condition consumption. Non-write reads (for example a
    /// <c>Console.WriteLine</c> argument) are intentionally ignored: only writes, escapes, and
    /// condition consumptions disqualify or associate the direct-local shape.
    /// </summary>
    private static void UpdateLocalUsage(
        Dictionary<ILocalSymbol, LocalUsageSummary> usage,
        ILocalReferenceOperation reference)
    {
        var local = reference.Local;
        if (!usage.TryGetValue(local, out var current))
        {
            current = new LocalUsageSummary(0, false, 0);
            usage[local] = current;
        }

        if (IsWriteReference(reference))
        {
            usage[local] = current with { WriteCount = current.WriteCount + 1 };
        }
        else if (IsByReferenceEscape(reference))
        {
            usage[local] = current with { HasByReferenceEscape = true };
        }
        else if (IsIfConditionConsumption(reference))
        {
            usage[local] = current with { IfConditionConsumptionCount = current.IfConditionConsumptionCount + 1 };
        }
    }

    /// <summary>
    /// True when the local reference is the write target of a simple assignment, compound assignment,
    /// or increment/decrement operation. The declaration-initializer write is counted separately on
    /// the declarator operation, so a later reassignment, compound write, or increment/decrement
    /// makes the total write count exceed one and fails the single-write shape closed.
    /// </summary>
    private static bool IsWriteReference(ILocalReferenceOperation reference)
    {
        IOperation? parent = reference.Parent;
        while (parent is IConversionOperation { IsImplicit: true })
        {
            parent = parent.Parent;
        }

        return parent switch
        {
            ISimpleAssignmentOperation simple => ReferenceEquals(UnwrapImplicitConversions(simple.Target), reference),
            ICompoundAssignmentOperation compound => ReferenceEquals(UnwrapImplicitConversions(compound.Target), reference),
            IIncrementOrDecrementOperation increment => ReferenceEquals(UnwrapImplicitConversions(increment.Target), reference),
            _ => false,
        };
    }

    /// <summary>
    /// True when the local reference is the value of a ref/out/in argument. A by-reference escape is
    /// an unsupported write before association: the callee can mutate the local, so the direct
    /// single-write condition shape fails closed anywhere in the top-level body.
    /// </summary>
    private static bool IsByReferenceEscape(ILocalReferenceOperation reference)
    {
        IOperation? current = reference;
        while (current.Parent is IConversionOperation { IsImplicit: true })
        {
            current = current.Parent;
        }

        return current.Parent is IArgumentOperation argument
            && argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out or RefKind.In;
    }

    /// <summary>
    /// True when the local reference flows directly (through implicit conversions) into the condition
    /// of an actual <c>if</c> statement. While, do-while, for, foreach, and conditional-expression
    /// consumers never count as exact local condition consumption.
    /// </summary>
    private static bool IsIfConditionConsumption(ILocalReferenceOperation reference)
    {
        IOperation? current = reference;
        while (current.Parent is IConversionOperation { IsImplicit: true })
        {
            current = current.Parent;
        }

        return current.Parent is IConditionalOperation conditional
            && ReferenceEquals(UnwrapImplicitConversions(conditional.Condition), reference)
            && conditional.Syntax is IfStatementSyntax;
    }

    /// <summary>
    /// Enumerates the operation tree of one top-level statement without descending into nested
    /// function bodies (lambdas, local functions).
    /// </summary>
    private static IEnumerable<IOperation> EnumerateTopLevelOperations(IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.TryPop(out var operation))
        {
            yield return operation;
            if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                continue;
            }

            foreach (var child in operation.ChildOperations)
            {
                pending.Push(child);
            }
        }
    }

    private sealed record LocalUsageSummary(
        int WriteCount,
        bool HasByReferenceEscape,
        int IfConditionConsumptionCount);

    /// <summary>
    /// Resolves the stable identity and method evidence of the synthesized top-level method that owns
    /// one global statement. The compiler entry point is the deterministic anchor; a synthesized
    /// enclosing symbol without a metadata name fails closed with no identity.
    /// </summary>
    private static (MethodId MethodId, ImmutableArray<EvidenceRef> Evidence)? ResolveTopLevelMethod(
        Compilation compilation,
        SemanticModel model,
        GlobalStatementSyntax globalStatement,
        LoadedProject project,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        CancellationToken cancellationToken)
    {
        // The synthesized top-level method is the compiler entry point; GetEnclosingSymbol usually
        // resolves it, with the entry point as a deterministic fallback.
        var entryPoint = model.GetEnclosingSymbol(globalStatement.SpanStart, cancellationToken) as IMethodSymbol
            ?? compilation.GetEntryPoint(cancellationToken);
        if (entryPoint is null)
        {
            return null;
        }

        var identityMethod = entryPoint.PartialDefinitionPart ?? entryPoint;
        if (string.IsNullOrWhiteSpace(identityMethod.MetadataName))
        {
            // A synthesized top-level enclosing symbol without a metadata name cannot produce a
            // stable method identity; the companion-only registration traversal fails closed for it.
            return null;
        }

        var methodId = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
            identityMethod,
            ResolveProject(identityMethod, project.StableId, projectsByAssembly)));
        return (methodId, CreateMethodEvidence(identityMethod, documents));
    }

    /// <summary>
    /// Projects accepted contract configuration condition facts and accepted contract conditional DI arm facts for one global
    /// statement. An admitted read that flows through exactly one compiler-bound local assigned once
    /// directly into a top-level <c>if</c> boolean condition anchors a condition fact and the exact
    /// registrations directly enclosed by that <c>if</c>/<c>else</c> project arm facts with the same
    /// condition/read operations. The single-write, unescaped direct-local shape is proven against
    /// the whole top-level body: any reassignment, compound/increment write, ref/out escape, or
    /// multiple condition consumption fails closed and withholds the condition, arms, and group while
    /// preserving the accepted contract read itself (regression). A conditional whose arm contains a nested control
    /// boundary (conditional, loop, switch, try/catch/finally, using/lock, nested function, or
    /// conditional expression) can never guarantee its registrations, so the whole conditional fails
    /// closed with no arm facts (regression). Extracted methods never participate.
    /// </summary>
    private static void CollectTopLevelConditionalFacts(
        LoadedProject project,
        GlobalStatementSyntax globalStatement,
        SemanticModel model,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        MethodId methodId,
        ImmutableArray<EvidenceRef> methodEvidence,
        List<TopLevelReadDraft> readDrafts,
        Dictionary<ILocalSymbol, LocalUsageSummary> localUsage,
        RoslynConfigurationSemanticFactCollector configurationFacts,
        RoslynConditionalDependencyInjectionFactCollector conditionalDependencyInjectionFacts,
        CancellationToken cancellationToken)
    {
        // Map each compiler-bound local to the admitted read that assigns it. A local assigned from
        // more than one admitted read, or assigned through any other shape, never anchors a condition.
        var localToRead = new Dictionary<ILocalSymbol, TopLevelReadDraft>(SymbolEqualityComparer.Default);
        var ambiguousLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        foreach (var draft in readDrafts)
        {
            if (draft.AssignedLocal is not { } local)
            {
                continue;
            }

            if (ambiguousLocals.Contains(local))
            {
                continue;
            }

            if (localToRead.Remove(local))
            {
                ambiguousLocals.Add(local);
            }
            else
            {
                localToRead[local] = draft;
            }
        }

        foreach (var ifStatement in globalStatement
                     .DescendantNodes()
                     .OfType<IfStatementSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsInsideNestedFunction(ifStatement)
                || model.GetOperation(ifStatement, cancellationToken) is not IConditionalOperation conditional)
            {
                continue;
            }

            var conditionValue = UnwrapImplicitConversions(conditional.Condition);
            if (conditionValue is not ILocalReferenceOperation conditionLocal
                || !localToRead.TryGetValue(conditionLocal.Local, out var read))
            {
                continue;
            }

            // regression: the direct-local shape requires exactly one write (the read assignment), no
            // ref/out escape, and exactly one actual if-condition consumption anywhere in the
            // top-level body. Any reassignment or escape fails closed and withholds the condition,
            // arms, and group while the admitted read itself still projects.
            if (!localUsage.TryGetValue(conditionLocal.Local, out var usage)
                || usage.WriteCount != 1
                || usage.HasByReferenceEscape
                || usage.IfConditionConsumptionCount != 1)
            {
                continue;
            }

            // The condition operation is the real source-backed if-condition operation of the
            // synthesized top-level method; the same identity anchors the configuration condition and
            // every arm fact of this if statement.
            var conditionId = CreateOperationId(
                conditional.Condition,
                methodId,
                MapKind(conditional.Condition).ToString(),
                blockOrdinal: 0,
                evaluationOrdinal: 0,
                siblingOrdinal: 0,
                documents);
            configurationFacts.AddCondition(
                methodId,
                read.ReadOperation,
                conditionId,
                CombineEvidence(
                    ResolveEvidence(read.Call, documents, methodEvidence),
                    ResolveEvidence(conditional.Condition, documents, methodEvidence)));

            // regression: when either arm contains a nested control boundary, the outer arm cannot
            // guarantee any registration, so the whole conditional yields no arm facts. Direct
            // block/expression-statement registrations remain admitted only for a pure alternative.
            if (!IsDirectAlternativeConditional(conditional))
            {
                continue;
            }

            if (conditional.WhenTrue is not null)
            {
                CollectTopLevelArmRegistrations(
                    project,
                    methodId,
                    conditional.WhenTrue,
                    isTrueArm: true,
                    conditionId,
                    read,
                    documents,
                    methodEvidence,
                    conditionalDependencyInjectionFacts,
                    cancellationToken);
            }

            if (conditional.WhenFalse is not null)
            {
                CollectTopLevelArmRegistrations(
                    project,
                    methodId,
                    conditional.WhenFalse,
                    isTrueArm: false,
                    conditionId,
                    read,
                    documents,
                    methodEvidence,
                    conditionalDependencyInjectionFacts,
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// True when both arms of the conditional contain only direct statements — no nested control
    /// boundary. A registration inside an inner conditional, loop, switch, try/catch/finally,
    /// using/lock, nested function, or conditional expression is never directly enclosed by the outer
    /// arm (regression).
    /// </summary>
    private static bool IsDirectAlternativeConditional(IConditionalOperation conditional)
        => (conditional.WhenTrue is null || HasOnlyDirectStatements(conditional.WhenTrue))
            && (conditional.WhenFalse is null || HasOnlyDirectStatements(conditional.WhenFalse));

    /// <summary>
    /// True when every direct statement of one arm is not a nested control boundary. Non-registration
    /// direct statements (assignments, helper calls, local declarations) are allowed; only control
    /// boundaries and conditional-expression-wrapped registration receivers fail the arm closed.
    /// </summary>
    private static bool HasOnlyDirectStatements(IOperation arm)
    {
        foreach (var statement in GetDirectStatements(arm))
        {
            if (IsNestedControlBoundary(statement))
            {
                return false;
            }

            // "Conditional expression as appropriate": a registration whose receiver/arguments flow
            // through a conditional expression is not guaranteed by the outer arm.
            if (TryGetRegistrationInvocation(statement, out var invocation)
                && ContainsConditionalExpression(invocation))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the direct statement operations of one arm: the block children, or the arm itself when
    /// it is not a block (for example an unbraced <c>if (x) AddScoped&lt;...&gt;(...);</c>).
    /// </summary>
    private static IEnumerable<IOperation> GetDirectStatements(IOperation arm)
    {
        if (arm is IBlockOperation block)
        {
            foreach (var statement in block.Operations)
            {
                yield return statement;
            }
        }
        else
        {
            yield return arm;
        }
    }

    /// <summary>
    /// True when the direct statement is a nested control boundary: conditional, loop, switch,
    /// try/catch/finally, using, lock, or a nested function body. Registrations inside any such
    /// boundary are never attributed to the outer arm (regression). No new control semantics are added:
    /// this is purely a fail-closed attribution boundary.
    /// </summary>
    private static bool IsNestedControlBoundary(IOperation operation)
        => operation is IConditionalOperation
            or ILoopOperation
            or ISwitchOperation
            or ITryOperation
            or IUsingOperation
            or ILockOperation
            or IAnonymousFunctionOperation
            or ILocalFunctionOperation;

    /// <summary>
    /// True when the operation tree (excluding nested function bodies) contains a conditional
    /// expression. A direct registration whose receiver or arguments flow through a conditional
    /// expression is not guaranteed by the outer arm.
    /// </summary>
    private static bool ContainsConditionalExpression(IOperation operation)
    {
        var pending = new Stack<IOperation>();
        pending.Push(operation);
        while (pending.TryPop(out var current))
        {
            if (current.Syntax is ConditionalExpressionSyntax)
            {
                return true;
            }

            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                continue;
            }

            foreach (var child in current.ChildOperations)
            {
                pending.Push(child);
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the registration invocation of one direct arm statement. The statement must be an
    /// expression statement whose expression unwraps (through implicit conversions) to an invocation;
    /// anything else yields no registration.
    /// </summary>
    private static bool TryGetRegistrationInvocation(IOperation statement, out IInvocationOperation invocation)
    {
        invocation = null!;
        if (statement is not IExpressionStatementOperation expressionStatement)
        {
            return false;
        }

        if (UnwrapImplicitConversions(expressionStatement.Operation) is not IInvocationOperation call)
        {
            return false;
        }

        invocation = call;
        return true;
    }

    /// <summary>
    /// Projects conditional DI arm facts for the exact registrations directly enclosed by one arm of a
    /// top-level <c>if</c> statement whose condition is admitted and whose arms contain no nested
    /// control boundary. Only direct expression-statement registrations are considered; the
    /// conditional collector's exact admission decides whether each invocation is a supported
    /// registration, and unsupported shapes fail closed.
    /// </summary>
    private static void CollectTopLevelArmRegistrations(
        LoadedProject project,
        MethodId methodId,
        IOperation armOperation,
        bool isTrueArm,
        OperationId conditionId,
        TopLevelReadDraft read,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        RoslynConditionalDependencyInjectionFactCollector conditionalDependencyInjectionFacts,
        CancellationToken cancellationToken)
    {
        foreach (var statement in GetDirectStatements(armOperation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetRegistrationInvocation(statement, out var invocation))
            {
                continue;
            }

            conditionalDependencyInjectionFacts.AddArm(
                project.StableId,
                methodId,
                invocation,
                CreateOperationId(
                    invocation,
                    methodId,
                    "Invocation",
                    blockOrdinal: 0,
                    evaluationOrdinal: 0,
                    siblingOrdinal: 0,
                    documents),
                conditionId,
                read.ReadOperation,
                read.Key,
                ResolveEvidence(invocation, documents, methodEvidence),
                isTrueArm);
        }
    }

    /// <summary>
    /// True when the syntax node is inside a lambda or local-function body. Only top-level statements
    /// are companion control authorities; nested function bodies never project arm facts.
    /// </summary>
    private static bool IsInsideNestedFunction(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideNestedFunction(IOperation operation)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the local assigned directly from one admitted read invocation through the syntax
    /// parent chain. Both the declaration-initializer shape
    /// (<c>bool x = configuration.GetValue&lt;bool&gt;(...)</c>) and the separate-assignment shape are
    /// accepted; any other consumer fails closed. The resolution is syntax+semantic-model based
    /// because <see cref="SemanticModel.GetOperation(SyntaxNode)"/> roots the operation tree at the
    /// invocation, so the operation-parent chain is unavailable for a standalone read expression.
    /// </summary>
    private static bool TryResolveAssignedLocal(SyntaxNode invocationSyntax, SemanticModel model, out ILocalSymbol local)
    {
        local = null!;
        SyntaxNode? current = invocationSyntax;
        while (current?.Parent is ParenthesizedExpressionSyntax)
        {
            current = current.Parent;
        }

        if (current?.Parent is EqualsValueClauseSyntax
            && current.Parent.Parent is VariableDeclaratorSyntax declarator
            && model.GetDeclaredSymbol(declarator) is ILocalSymbol declaredLocal)
        {
            local = declaredLocal;
            return true;
        }

        if (current?.Parent is AssignmentExpressionSyntax { RawKind: (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression } assignment
            && assignment.Left is IdentifierNameSyntax identifier
            && model.GetSymbolInfo(identifier).Symbol is ILocalSymbol assignedLocal)
        {
            local = assignedLocal;
            return true;
        }

        return false;
    }

    private sealed record TopLevelReadDraft(
        IInvocationOperation Call,
        OperationId ReadOperation,
        string Key,
        ILocalSymbol? AssignedLocal);

    /// <summary>
    /// Cached documents and semantic models of one loaded project from the deterministic pass-1
    /// preload. Pass 2 reuses this cache for the accepted extraction and companion collection so
    /// every project context exists before any callback method is registered (regression).
    /// </summary>
    private sealed record LoadedProjectContext(
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> Documents,
        Dictionary<SyntaxTree, SemanticModel> Models);

    private static bool IsExtractedMethod(IMethodSymbol method) =>
        method.Locations.Any(location => location.IsInSource)
        && (!method.IsImplicitlyDeclared
            || method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor);

    private static void ExtractOneBody(
        LoadedProject project,
        IMethodSymbol member,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        ImmutableArray<ExtractedMethodBody>.Builder bodies,
        ImmutableArray<TypeInstantiationFact>.Builder instantiations,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics,
        RoslynSemanticFactCollector semanticFacts,
        FrameworkAnalysisRequestCollector frameworkRequest,
        RoslynDependencyInjectionFactCollector dependencyInjectionFacts,
        RoslynStructuralResultFactCollector structuralResultFacts,
        RoslynNonGetSemanticFactCollector nonGetFacts,
        RoslynConfigurationSemanticFactCollector configurationFacts,
        RoslynCallbackBoundaryFactCollector callbackBoundaryFacts,
        RoslynPredicateFactCollector predicateFacts,
        CompilationProfileId profileId,
        CancellationToken cancellationToken)
    {
        var identityMethod = member.PartialDefinitionPart ?? member;
        var bodyMethod = identityMethod.PartialImplementationPart ?? identityMethod;
        var bodyOperation = FindBodyOperation(bodyMethod, models, cancellationToken);
        if (bodyOperation is null)
        {
            diagnostics.Add(CreateBodylessDiagnostic(member, project));
            return;
        }

        var methodId = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
            identityMethod,
            ResolveProject(identityMethod, project.StableId, projectsByAssembly)));
        var evidence = CreateMethodEvidence(bodyMethod, documents);
        var localInitializers = CollectMethodLocalInitializers(bodyOperation);
        var body = ExtractBody(
            identityMethod,
            methodId,
            bodyOperation,
            evidence,
            documents,
            models,
            projectsByAssembly,
            project.StableId,
            instantiations,
            semanticFacts,
            frameworkRequest,
            dependencyInjectionFacts,
            structuralResultFacts,
            nonGetFacts,
            configurationFacts,
            callbackBoundaryFacts,
            predicateFacts,
            profileId,
            localInitializers,
            diagnostics,
            cancellationToken);
        bodies.Add(body);
    }

    private static AnalysisDiagnostic CreateBodylessDiagnostic(IMethodSymbol method, LoadedProject project)
    {
        var identityMethod = method.PartialDefinitionPart ?? method;
        var methodId = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
            identityMethod,
            project.StableId));
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            "BE1001",
            AnalysisStage.BaselineIndex,
            null,
            methodId.Value,
            0));
        return new AnalysisDiagnostic(
            id,
            "BE1001",
            SeqDoc.Core.Diagnostics.DiagnosticSeverity.Warning,
            AnalysisStage.BaselineIndex,
            "A source method has no extractable body and was skipped.",
            new DiagnosticLocation("behavior extraction", symbol: new SymbolId(methodId.Value)),
            "The method declares no analyzable source body (for example an abstract or extern method).",
            "No behavior facts are produced for this method.",
            "Treat the method's source behavior as unavailable.",
            CertaintyLevel.Exact);
    }

    private static IMethodBodyOperation? FindBodyOperation(
        IMethodSymbol method,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        CancellationToken cancellationToken)
    {
        foreach (var reference in method.DeclaringSyntaxReferences
                     .OrderBy(reference => reference.Span.Start))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = reference.GetSyntax(cancellationToken);
            if (syntax is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax
                && models.TryGetValue(syntax.SyntaxTree, out var model))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return model.GetOperation(syntax, cancellationToken) as IMethodBodyOperation;
            }
        }

        return null;
    }

    /// <summary>
    /// Collects only compiler-bound locals whose declaration initializer is unique and whose method
    /// operation tree contains no later write or by-reference escape. This companion map is used for
    /// receiver-chain projection only; it does not participate in Method Flow identity generation.
    /// </summary>
    private static Dictionary<ILocalSymbol, IOperation> CollectMethodLocalInitializers(
        IMethodBodyOperation body)
    {
        var candidates = new Dictionary<ILocalSymbol, IOperation>(SymbolEqualityComparer.Default);
        var duplicate = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var reassigned = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var operation in EnumerateSelfAndChildren(body))
        {
            if (operation is IVariableDeclaratorOperation
                {
                    Initializer: { Value: { } initializer },
                    Symbol: ILocalSymbol local,
                })
            {
                if (!candidates.TryAdd(local, initializer))
                {
                    candidates.Remove(local);
                    duplicate.Add(local);
                }
            }

            if (operation is ILocalReferenceOperation reference
                && (IsWriteReference(reference) || IsByReferenceEscape(reference)))
            {
                reassigned.Add(reference.Local);
            }
        }

        var result = new Dictionary<ILocalSymbol, IOperation>(SymbolEqualityComparer.Default);
        foreach (var pair in candidates)
        {
            if (!duplicate.Contains(pair.Key) && !reassigned.Contains(pair.Key))
            {
                result.Add(pair.Key, pair.Value);
            }
        }

        return result;
    }

    private static ExtractedMethodBody ExtractBody(
        IMethodSymbol method,
        MethodId methodId,
        IMethodBodyOperation bodyOperation,
        ImmutableArray<EvidenceRef> evidence,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        StableProjectId project,
        ImmutableArray<TypeInstantiationFact>.Builder instantiations,
        RoslynSemanticFactCollector semanticFacts,
        FrameworkAnalysisRequestCollector frameworkRequest,
        RoslynDependencyInjectionFactCollector dependencyInjectionFacts,
        RoslynStructuralResultFactCollector structuralResultFacts,
        RoslynNonGetSemanticFactCollector nonGetFacts,
        RoslynConfigurationSemanticFactCollector configurationFacts,
        RoslynCallbackBoundaryFactCollector callbackBoundaryFacts,
        RoslynPredicateFactCollector predicateFacts,
         CompilationProfileId profileId,
         IReadOnlyDictionary<ILocalSymbol, IOperation> localInitializers,
         ImmutableArray<AnalysisDiagnostic>.Builder diagnostics,
         CancellationToken cancellationToken)
    {
        var cfg = ControlFlowGraph.Create(bodyOperation, cancellationToken);

        var regionBuilder = ImmutableArray.CreateBuilder<ExtractedExceptionRegion>();
        var regionById = new Dictionary<ControlFlowRegion, FlowRegionId>(ReferenceEqualityComparer.Instance);
        var regionOrdinal = 0;
        VisitRegion(cfg.Root, null, regionBuilder, regionById, ref regionOrdinal, methodId, evidence);

        var operations = ImmutableArray.CreateBuilder<ExtractedOperation>();
        var blocks = ImmutableArray.CreateBuilder<ExtractedBasicBlock>();
        var operationById = new Dictionary<IOperation, OperationId>(ReferenceEqualityComparer.Instance);
        var realThrowBlocks = ComputeRealThrows(bodyOperation, cfg);
        var siblingOrdinals = new Dictionary<(string Kind, int BlockOrdinal), int>();
        var evaluationOrdinal = 0;

        foreach (var block in cfg.Blocks.OrderBy(block => block.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operationIds = ImmutableArray.CreateBuilder<OperationId>();
            foreach (var operation in block.Operations)
            {
                FlattenOperation(
                    operation,
                    parent: null,
                    methodId,
                    block.Ordinal,
                    ref evaluationOrdinal,
                    siblingOrdinals,
                    operationById,
                    operations,
                    instantiations,
                    documents,
                    projectsByAssembly,
                     project,
                     evidence,
                     localInitializers);
                if (operationById.TryGetValue(operation, out var id))
                {
                    operationIds.Add(id);
                }
            }

            OperationId? branchCondition = null;
            if (block.BranchValue is not null)
            {
                var branchId = FlattenOperation(
                    block.BranchValue,
                    parent: null,
                    methodId,
                    block.Ordinal,
                    ref evaluationOrdinal,
                    siblingOrdinals,
                    operationById,
                    operations,
                    instantiations,
                    documents,
                    projectsByAssembly,
                     project,
                     evidence,
                     localInitializers);
                if (block.ConditionalSuccessor is not null)
                {
                    branchCondition = branchId;
                }
                else
                {
                    operationIds.Add(branchId);
                }
            }

            var terminal = ResolveTerminal(block);
            bool escapingThrow = terminal switch
            {
                ExtractedBlockTerminalKind.Throw when realThrowBlocks.TryGetValue(block.Ordinal, out var thrownType) =>
                    !IsCaughtByEnclosingTry(block.Ordinal, cfg.Root, thrownType, excludedRegion: null),
                ExtractedBlockTerminalKind.Throw => false,
                ExtractedBlockTerminalKind.Rethrow =>
                    !IsCaughtByEnclosingTry(
                        block.Ordinal,
                        cfg.Root,
                        FindEnclosingCatch(block.Ordinal, cfg.Root)?.ExceptionType,
                        FindEnclosingCatch(block.Ordinal, cfg.Root)?.EnclosingRegion),
                _ => false,
            };

            var predecessorOrdinals = block.Predecessors
                .Select(branch => branch.Source?.Ordinal)
                .Where(ordinal => ordinal is not null)
                .Select(ordinal => ordinal!.Value)
                .Distinct()
                .Order()
                .ToImmutableArray();
            var enteringRegions = block.Predecessors
                .SelectMany(branch => branch.EnteringRegions)
                .Where(region => regionById.ContainsKey(region))
                .Select(region => regionById[region])
                .Distinct()
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToImmutableArray();
            var leavingRegions = block.Predecessors
                .SelectMany(branch => branch.LeavingRegions)
                .Where(region => regionById.ContainsKey(region))
                .Select(region => regionById[region])
                .Distinct()
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToImmutableArray();

            blocks.Add(new ExtractedBasicBlock(
                block.Ordinal,
                operationIds.ToImmutable(),
                branchCondition,
                block.FallThroughSuccessor?.Destination?.Ordinal,
                block.ConditionalSuccessor?.Destination is null
                    ? []
                    : [block.ConditionalSuccessor.Destination.Ordinal],
                predecessorOrdinals,
                terminal,
                escapingThrow,
                enteringRegions,
                leavingRegions,
                evidence,
                CertaintyLevel.Exact));
        }

        // Loop operations are compiler anchors, not CFG operations. Generate their identities in a
        // side map only: adding them to the flattened operation stream changes value flow, evaluation
        // order, and unrelated fingerprints.
        var loopAnchors = new Dictionary<ILoopOperation, OperationId>(ReferenceEqualityComparer.Instance);
        var extractedAnchors = ImmutableArray.CreateBuilder<ExtractedLoopAnchor>();
        foreach (var loop in EnumerateLoopOperations(bodyOperation))
        {
            var anchorId = CreateOperationId(loop, methodId, "LoopAnchor", 0, loop.Syntax?.SpanStart ?? 0, 0, documents);
            loopAnchors[loop] = anchorId;
            extractedAnchors.Add(new ExtractedLoopAnchor(anchorId, loop switch
            {
                IWhileLoopOperation whileLoop when !whileLoop.ConditionIsTop => ExtractedLoopKind.DoWhileLoop,
                IWhileLoopOperation => ExtractedLoopKind.WhileLoop,
                IForEachLoopOperation => ExtractedLoopKind.ForEachLoop,
                _ => ExtractedLoopKind.ForLoop,
            }, ResolveEvidence(loop, documents, evidence),
                loop.Syntax is null ? CertaintyLevel.Unknown : CertaintyLevel.Exact));
        }
        var ordinaryBranches = BuildOrdinaryBranches(cfg, regionById, documents, evidence);

        var locals = CollectLocals(bodyOperation)
            .Keys
            .Select(local => new ExtractedLocal(local.Name, local.Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat)))
            .OrderBy(local => local.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        var parameters = method.Parameters
            .Select(parameter => new ExtractedParameter(
                parameter.Name,
                parameter.Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
                RoslynProgramIndexExtractor.ToParameterRefKind(parameter.RefKind)))
            .ToImmutableArray();

        var body = new ExtractedMethodBody(
            methodId,
            string.Empty,
            parameters,
            locals,
            operations.ToImmutable(),
            blocks.ToImmutable(),
            regionBuilder.ToImmutable(),
            evidence,
            BuildNaturalLoops(
                cfg,
                bodyOperation,
                ordinaryBranches,
                loopAnchors,
                methodId,
                regionById,
                evidence,
                documents,
                diagnostics),
            extractedAnchors.OrderBy(anchor => anchor.Operation.Value, StringComparer.Ordinal).ToImmutableArray(),
            ordinaryBranches);
        CollectSemanticFacts(
            cfg,
            method,
            methodId,
            operationById,
            models,
            documents,
            methodEvidence: evidence,
            project,
            projectsByAssembly,
            semanticFacts,
            frameworkRequest,
            dependencyInjectionFacts,
            structuralResultFacts,
            nonGetFacts,
            configurationFacts,
            predicateFacts,
            profileId,
            localInitializers,
            cancellationToken);
        // Callback-boundary companion facts are registered exactly once per extracted source method
        // body, reusing the same compiler-bound body operation, stable method identity, method
        // evidence, and authoritative operation map as the accepted behavior input so every callback
        // boundary stays deterministic and evidence-backed without altering Method Flow or behavior
        // fingerprints. Exact boundary analysis is deferred to the collector's Build, after every
        // loaded project context and method body is registered (regression).
        callbackBoundaryFacts.AddMethod(project, methodId, bodyOperation, evidence, operationById, cancellationToken);
        return body with { BodyFingerprint = BehaviorFingerprint.ComputeBody(body) };
    }

    private static ImmutableArray<ExtractedOrdinaryBranch> BuildOrdinaryBranches(
        ControlFlowGraph cfg,
        Dictionary<ControlFlowRegion, FlowRegionId> regionById,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence) =>
        cfg.Blocks
            .SelectMany(block => new[] { block.FallThroughSuccessor, block.ConditionalSuccessor }
                .Where(branch => branch is not null)
                .Select(branch => (Source: block, Branch: branch!)))
            .Where(item => item.Source.IsReachable
                && item.Branch.Destination?.IsReachable == true
                && item.Branch.Semantics == ControlFlowBranchSemantics.Regular
                && item.Branch.Destination is not null)
            .Select(item => new ExtractedOrdinaryBranch(
                item.Source.Ordinal,
                item.Branch.Destination!.Ordinal,
                item.Branch.EnteringRegions.Where(regionById.ContainsKey).Select(region => regionById[region]).Distinct().OrderBy(id => id.Value, StringComparer.Ordinal).ToImmutableArray(),
                item.Branch.LeavingRegions.Where(regionById.ContainsKey).Select(region => regionById[region]).Distinct().OrderBy(id => id.Value, StringComparer.Ordinal).ToImmutableArray(),
                BlockEvidence(item.Source, documents, methodEvidence)
                    .AddRange(BlockEvidence(item.Branch.Destination, documents, methodEvidence))
                    .DistinctBy(evidence => evidence.Id)
                    .OrderBy(evidence => evidence.Id.Value, StringComparer.Ordinal)
                    .ToImmutableArray(),
                CertaintyLevel.Exact))
            .OrderBy(branch => branch.SourceBlockOrdinal)
            .ThenBy(branch => branch.DestinationBlockOrdinal)
            .ToImmutableArray();

    private static ImmutableArray<EvidenceRef> BlockEvidence(
        BasicBlock block,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence)
    {
        if (block.BranchValue is { } branchValue)
        {
            return ResolveEvidence(branchValue, documents, methodEvidence);
        }

        if (block.Operations.FirstOrDefault() is { } operation)
        {
            return ResolveEvidence(operation, documents, methodEvidence);
        }

        return methodEvidence;
    }

    private static ImmutableArray<ExtractedNaturalLoop> BuildNaturalLoops(
        ControlFlowGraph cfg,
        IMethodBodyOperation bodyOperation,
        ImmutableArray<ExtractedOrdinaryBranch> branches,
        Dictionary<ILoopOperation, OperationId> loopAnchors,
        MethodId methodId,
        Dictionary<ControlFlowRegion, FlowRegionId> regionById,
        ImmutableArray<EvidenceRef> methodEvidence,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics)
    {
        var blocks = cfg.Blocks.ToDictionary(block => block.Ordinal);
        var successors = branches.GroupBy(branch => branch.SourceBlockOrdinal).ToDictionary(group => group.Key, group => group.Select(branch => branch.DestinationBlockOrdinal).ToArray());
        var predecessors = branches.GroupBy(branch => branch.DestinationBlockOrdinal).ToDictionary(group => group.Key, group => group.Select(branch => branch.SourceBlockOrdinal).ToArray());
        var entryOrdinal = blocks.Keys.Min();
        var roots = blocks.Keys.Where(ordinal => ordinal == entryOrdinal || !predecessors.ContainsKey(ordinal)).Order().ToArray();
        var componentByBlock = new Dictionary<int, int>();
        foreach (var root in roots)
        {
            var pending = new Stack<int>();
            pending.Push(root);
            while (pending.TryPop(out var current))
            {
                if (componentByBlock.ContainsKey(current))
                {
                    continue;
                }
                componentByBlock[current] = root;
                if (successors.TryGetValue(current, out var next))
                {
                    foreach (var destination in next.Order())
                    {
                        pending.Push(destination);
                    }
                }
            }
        }

        var dominators = new Dictionary<int, HashSet<int>>();
        foreach (var component in componentByBlock.Values.Distinct().Order())
        {
            var members = componentByBlock.Where(pair => pair.Value == component).Select(pair => pair.Key).Order().ToArray();
            var all = members.ToHashSet();
            foreach (var member in members)
            {
                dominators[member] = member == component ? [member] : new HashSet<int>(all);
            }
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var member in members.Where(member => member != component))
                {
                    var incoming = predecessors.GetValueOrDefault(member, []).Where(dominators.ContainsKey).Where(predecessor => componentByBlock[predecessor] == component).ToArray();
                    if (incoming.Length == 0)
                    {
                        continue;
                    }
                    var value = new HashSet<int>(dominators[incoming[0]]);
                    foreach (var predecessor in incoming.Skip(1))
                    {
                        value.IntersectWith(dominators[predecessor]);
                    }
                    value.Add(member);
                    if (!value.SetEquals(dominators[member]))
                    {
                        dominators[member] = value;
                        changed = true;
                    }
                }
            }
        }

        var candidates = new List<(int Header, HashSet<int> Members, List<ExtractedOrdinaryBranch> BackEdges)>();
        foreach (var backEdge in branches.Where(branch => dominators.GetValueOrDefault(branch.SourceBlockOrdinal)?.Contains(branch.DestinationBlockOrdinal) == true))
        {
            var members = new HashSet<int> { backEdge.DestinationBlockOrdinal, backEdge.SourceBlockOrdinal };
            var pending = new Stack<int>();
            pending.Push(backEdge.SourceBlockOrdinal);
            while (pending.TryPop(out var current))
            {
                if (current == backEdge.DestinationBlockOrdinal)
                {
                    continue;
                }

                foreach (var predecessor in predecessors.GetValueOrDefault(current, []))
                {
                    if (predecessor != backEdge.DestinationBlockOrdinal && members.Add(predecessor))
                    {
                        pending.Push(predecessor);
                    }
                }
            }

            var existing = candidates.FirstOrDefault(candidate => candidate.Header == backEdge.DestinationBlockOrdinal && candidate.Members.SetEquals(members));
            if (existing.Members is not null)
            {
                existing.BackEdges.Add(backEdge);
            }
            else
            {
                candidates.Add((backEdge.DestinationBlockOrdinal, members, [backEdge]));
            }
        }

        var loopOperations = EnumerateLoopOperations(bodyOperation)
            .Where(loopAnchors.ContainsKey)
            .Select(operation => (Operation: operation, Id: loopAnchors[operation]))
            .ToArray();
        var normalizedCandidates = candidates
                     .GroupBy(candidate => candidate.Header)
                     .Select(group => (
                         Header: group.Key,
                         Members: group.SelectMany(candidate => candidate.Members).ToHashSet(),
                         BackEdges: group.SelectMany(candidate => candidate.BackEdges).ToList()))
                     .OrderBy(candidate => candidate.Header)
                      .ThenBy(candidate => candidate.Members.Min())
                      .ToArray();
        var result = new List<ExtractedNaturalLoop>();
        var usedAnchors = new HashSet<OperationId>();
        var rejectedAnchors = new HashSet<OperationId>();
        foreach (var candidate in normalizedCandidates)
        {
            if (candidate.Members.Any(member => componentByBlock.GetValueOrDefault(member) != componentByBlock.GetValueOrDefault(candidate.Header)))
            {
                diagnostics.Add(CreateLoopDiagnostic(bodyOperation, methodId, "irreducible or cross-component candidate", candidate.Header));
                continue;
            }
            var entries = branches.Where(branch => candidate.Members.Contains(branch.DestinationBlockOrdinal) && !candidate.Members.Contains(branch.SourceBlockOrdinal) && branch.DestinationBlockOrdinal != candidate.Header).ToArray();
            if (entries.Length > 0)
            {
                diagnostics.Add(CreateLoopDiagnostic(bodyOperation, methodId, "multi-entry candidate", candidate.Header));
                continue;
            }
            var matches = loopOperations
                .Where(item => !usedAnchors.Contains(item.Id) && MatchesExactShape(item.Operation, candidate, blocks))
                .ToArray();
            if (matches.Length != 1)
            {
                foreach (var matchingAnchor in matches)
                {
                    rejectedAnchors.Add(matchingAnchor.Id);
                }
                diagnostics.Add(CreateLoopDiagnostic(bodyOperation, methodId, "duplicate or inconsistent anchor mapping", candidate.Header));
                continue;
            }
            var match = matches[0];
            usedAnchors.Add(match.Id);
            var kind = match.Operation switch
            {
                IWhileLoopOperation whileLoop when !whileLoop.ConditionIsTop => ExtractedLoopKind.DoWhileLoop,
                IWhileLoopOperation => ExtractedLoopKind.WhileLoop,
                IForEachLoopOperation => ExtractedLoopKind.ForEachLoop,
                _ => ExtractedLoopKind.ForLoop,
            };
            var exits = branches.Where(branch => candidate.Members.Contains(branch.SourceBlockOrdinal) && !candidate.Members.Contains(branch.DestinationBlockOrdinal)).Select(branch => branch.DestinationBlockOrdinal).Distinct().Order().ToImmutableArray();
            var loopEvidence = ResolveEvidence(match.Operation, documents, methodEvidence)
                .AddRange(candidate.BackEdges.SelectMany(edge => edge.Evidence))
                .DistinctBy(evidence => evidence.Id)
                .OrderBy(evidence => evidence.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray();
            result.Add(new ExtractedNaturalLoop(match.Id, kind, candidate.Header,
                candidate.BackEdges.Select(edge => edge.SourceBlockOrdinal).Distinct().Order().ToImmutableArray(),
                candidate.Members.Where(member => member != candidate.Header).Order().ToImmutableArray(), exits,
                candidate.BackEdges.OrderBy(edge => edge.SourceBlockOrdinal).ThenBy(edge => edge.DestinationBlockOrdinal).ToImmutableArray(),
                loopEvidence, WeakestCertainty(match.Operation, candidate)));
        }
        foreach (var unmatched in loopOperations.Where(item => !usedAnchors.Contains(item.Id) && !rejectedAnchors.Contains(item.Id)))
        {
            diagnostics.Add(CreateLoopDiagnostic(bodyOperation, methodId, "unmatched compiler loop operation", unmatched.Operation.Syntax?.SpanStart ?? 0));
        }
        return result.OrderBy(loop => loop.HeaderBlockOrdinal).ThenBy(loop => loop.LoopOperation.Value, StringComparer.Ordinal).ToImmutableArray();

        static CertaintyLevel WeakestCertainty(ILoopOperation operation, (int Header, HashSet<int> Members, List<ExtractedOrdinaryBranch> BackEdges) candidate)
        {
            var values = candidate.BackEdges.Select(edge => edge.Certainty)
                .Append(operation.Syntax is null ? CertaintyLevel.Unknown : CertaintyLevel.Exact);
            return values.Max();
        }

        static bool MatchesExactShape(ILoopOperation operation, (int Header, HashSet<int> Members, List<ExtractedOrdinaryBranch> BackEdges) candidate, Dictionary<int, BasicBlock> blocks)
        {
            var condition = operation switch
            {
                IWhileLoopOperation whileLoop => whileLoop.Condition,
                IForLoopOperation forLoop => forLoop.Condition,
                _ => null,
            };
            var conditionBlocks = condition is null ? [] : blocks.Values
                .Where(block => block.BranchValue is not null)
                .Where(block => block.BranchValue!.Kind == condition.Kind
                    && block.BranchValue.Syntax is not null
                    && condition.Syntax is not null
                    && block.BranchValue.Syntax.SyntaxTree == condition.Syntax.SyntaxTree
                    && block.BranchValue.Syntax.Span == condition.Syntax.Span)
                .Select(block => block.Ordinal)
                .Order()
                .ToArray();
            int? conditionBlock = conditionBlocks.Length == 1 ? conditionBlocks[0] : null;
            if (condition is not null && conditionBlocks.Length != 1)
            {
                return false;
            }
            if (operation is IWhileLoopOperation whileOperation && !whileOperation.ConditionIsTop)
            {
                return conditionBlock is { } doWhileConditionBlock
                    && candidate.BackEdges.Count(edge => edge.SourceBlockOrdinal == doWhileConditionBlock
                    && edge.DestinationBlockOrdinal == candidate.Header) == 1;
            }

            if (conditionBlock is { } testedConditionBlock)
            {
                return testedConditionBlock == candidate.Header;
            }

            // Array foreach lowers to an implicit, zero-argument Boolean Invocation branch value over
            // the exact collection expression. The compiler-generated flag, branch value kind, syntax
            // identity, and header ordinal are admitted;
            // whole-body/member containment is not.
            return operation is IForEachLoopOperation foreachLoop
                && foreachLoop.Collection.Syntax is not null
                && blocks.Values.Where(block => block.BranchValue is IInvocationOperation invocation
                    && invocation.Kind == OperationKind.Invocation
                    && invocation.IsImplicit
                    && invocation.Arguments.Length == 0
                    && invocation.TargetMethod.ReturnType.SpecialType == SpecialType.System_Boolean
                    && invocation.Syntax is not null
                    && invocation.Syntax.SyntaxTree == foreachLoop.Collection.Syntax.SyntaxTree
                    && invocation.Syntax.Span == foreachLoop.Collection.Syntax.Span)
                    .Select(block => block.Ordinal)
                    .Distinct()
                    .ToArray() is { Length: 1 } collectionBlocks
                && collectionBlocks[0] == candidate.Header;

        }

        static AnalysisDiagnostic CreateLoopDiagnostic(IOperation operation, MethodId methodId, string reason, int ordinal)
        {
            var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor("BE2010", AnalysisStage.BaselineIndex, null, operation.Syntax?.ToString() ?? "loop", Math.Max(0, ordinal)));
            return new AnalysisDiagnostic(id, "BE2010", SeqDoc.Core.Diagnostics.DiagnosticSeverity.Warning, AnalysisStage.BaselineIndex,
                $"A compiler natural-loop candidate was withheld ({reason}).", new DiagnosticLocation($"behavior extraction ({methodId.Value})"),
                "The compiler control-flow facts do not prove one supported natural loop.",
                "No natural-loop descriptor is produced.", "Treat loop topology conservatively.", CertaintyLevel.Conservative,
                internalDetail: reason);
        }
    }

    /// <summary>
    /// Projects semantic companion facts from the same control-flow graph and operation traversal that
    /// produced the accepted behavior input, so every fact reuses stable MethodId and OperationId
    /// anchors. Source-backed facts only; arithmetic and unsupported shapes fail closed.
    /// </summary>
    private static void CollectSemanticFacts(
        ControlFlowGraph cfg,
        IMethodSymbol method,
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        StableProjectId project,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        RoslynSemanticFactCollector semanticFacts,
        FrameworkAnalysisRequestCollector frameworkRequest,
        RoslynDependencyInjectionFactCollector dependencyInjectionFacts,
        RoslynStructuralResultFactCollector structuralResultFacts,
        RoslynNonGetSemanticFactCollector nonGetFacts,
        RoslynConfigurationSemanticFactCollector configurationFacts,
        RoslynPredicateFactCollector predicateFacts,
        CompilationProfileId profileId,
        IReadOnlyDictionary<ILocalSymbol, IOperation> localInitializers,
        CancellationToken cancellationToken)
    {
        var loweredByCondition = new Dictionary<ConditionKey, PredicateConditionGroup>();
        foreach (var block in cfg.Blocks.OrderBy(block => block.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The control-flow graph models a return as a branch with Return semantics. An explicit
            // value return carries the returned expression as the block's branch value. Void and
            // no-value returns leave no branch value and emit no fact.
            if (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return
                && TryResolveReturnValue(block, operationById, out var returnValue, out var returnValueId))
            {
                semanticFacts.AddReturnProvenance(
                    methodId,
                    returnValueId,
                    ResolveEvidence(returnValue, documents, methodEvidence));
            }

            foreach (var operation in block.Operations)
            {
                CollectOperationSemanticFacts(
                    operation,
                    methodId,
                    operationById,
                    models,
                    documents,
                    methodEvidence,
                    project,
                    projectsByAssembly,
                    semanticFacts,
                    frameworkRequest,
                    dependencyInjectionFacts,
                    structuralResultFacts,
                    nonGetFacts,
                    profileId,
                    localInitializers,
                    cancellationToken);
            }

            if (block.BranchValue is not null)
            {
                CollectOperationSemanticFacts(
                    block.BranchValue,
                    methodId,
                    operationById,
                    models,
                    documents,
                    methodEvidence,
                    project,
                    projectsByAssembly,
                    semanticFacts,
                    frameworkRequest,
                    dependencyInjectionFacts,
                    structuralResultFacts,
                    nonGetFacts,
                    profileId,
                    localInitializers,
                    cancellationToken);
            }

            if (block.ConditionalSuccessor is not null
                && block.BranchValue is not null
                && operationById.TryGetValue(block.BranchValue, out var branchOperationId)
                && FindOwningCondition(block.BranchValue.Syntax) is { } conditionSyntax)
            {
                var key = new ConditionKey(
                    conditionSyntax.SyntaxTree,
                    conditionSyntax.SpanStart,
                    conditionSyntax.Span.Length);
                if (!loweredByCondition.TryGetValue(key, out var group))
                {
                    group = new PredicateConditionGroup(conditionSyntax);
                    loweredByCondition.Add(key, group);
                }
                group.Lowered.Add(branchOperationId);

                structuralResultFacts.AddDecision(
                    project,
                    methodId,
                    cfg,
                    block,
                    operationById,
                    methodEvidence,
                    cancellationToken);
            }
        }

        var groupOrdinal = 0;
        foreach (var group in loweredByCondition.Values
                     .OrderBy(group => group.Syntax.SyntaxTree.FilePath, StringComparer.Ordinal)
                     .ThenBy(group => group.Syntax.SpanStart)
                     .ThenBy(group => group.Syntax.Span.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!models.TryGetValue(group.Syntax.SyntaxTree, out var model)
                || !TryGetSourceConditionOperation(model, group.Syntax, cancellationToken, out var sourceRoot))
            {
                // Compiler-generated branches and branches without a source owner are deliberately
                // ignored. They are not source predicates and must not be guessed into one.
                continue;
            }

            var sourceOperation = CreateOperationId(
                sourceRoot,
                methodId,
                "PredicateSourceCondition",
                blockOrdinal: 0,
                evaluationOrdinal: 0,
                siblingOrdinal: groupOrdinal++,
                documents);
            predicateFacts.Add(
                methodId,
                sourceOperation,
                sourceRoot,
                ResolveEvidence(sourceRoot, documents, methodEvidence),
                group.Lowered
                    .Distinct()
                    .OrderBy(operation => operation.Value, StringComparer.Ordinal)
                    .ToImmutableArray());
        }

        CollectLambdaComparisonFacts(
            cfg,
            methodId,
            models,
            documents,
            methodEvidence,
            semanticFacts);
        CollectRelationalPatterns(
            cfg.OriginalOperation,
            methodId,
            operationById,
            models,
            documents,
            methodEvidence,
            nonGetFacts);
        CollectStatusSwitchFacts(
            cfg,
            method,
            project,
            methodId,
            operationById,
            models,
            documents,
            methodEvidence,
            nonGetFacts,
            cancellationToken);
        CollectDirectTerminalOutcomeFacts(
            cfg,
            method,
            project,
            methodId,
            operationById,
            models,
            documents,
            methodEvidence,
            nonGetFacts,
            cancellationToken);
        CollectSourceObservationFacts(
            cfg,
            methodId,
            operationById,
            models,
            documents,
            methodEvidence,
            nonGetFacts);
        CollectSourceOrderedOperationFacts(
            cfg.OriginalOperation,
            methodId,
            operationById,
            documents,
            methodEvidence,
            nonGetFacts);
        CollectConfigurationSemanticFacts(
            cfg,
            methodId,
            operationById,
            documents,
            methodEvidence,
            project,
            configurationFacts);
    }

    private static ExpressionSyntax? FindOwningCondition(SyntaxNode syntax)
    {
        foreach (var current in syntax.AncestorsAndSelf())
        {
            ExpressionSyntax? condition = current.Parent switch
            {
                IfStatementSyntax statement when ReferenceEquals(statement.Condition, current) => statement.Condition,
                WhileStatementSyntax statement when ReferenceEquals(statement.Condition, current) => statement.Condition,
                DoStatementSyntax statement when ReferenceEquals(statement.Condition, current) => statement.Condition,
                ForStatementSyntax statement when ReferenceEquals(statement.Condition, current) => statement.Condition,
                ConditionalExpressionSyntax expression when ReferenceEquals(expression.Condition, current) => expression.Condition,
                _ => null,
            };
            if (condition is not null)
            {
                return condition;
            }

            if (current is IfStatementSyntax ifStatement)
            {
                return ifStatement.Condition;
            }

            if (current is WhileStatementSyntax whileStatement)
            {
                return whileStatement.Condition;
            }

            if (current is DoStatementSyntax doStatement)
            {
                return doStatement.Condition;
            }

            if (current is ForStatementSyntax forStatement)
            {
                return forStatement.Condition;
            }

            if (current is ConditionalExpressionSyntax conditionalExpression)
            {
                return conditionalExpression.Condition;
            }
        }

        return null;
    }

    private static bool TryGetSourceConditionOperation(
        SemanticModel model,
        ExpressionSyntax condition,
        CancellationToken cancellationToken,
        out IOperation operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (model.GetOperation(condition, cancellationToken) is { } direct)
        {
            operation = direct;
            return true;
        }

        // Roslyn can omit an operation for a condition expression when the enclosing statement is
        // the operation anchor. The owner is still exact; recover its compiler condition rather than
        // associating a lowered fragment or reconstructing from descendants.
        if (condition.Parent is IfStatementSyntax { } ifStatement)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (model.GetOperation(ifStatement, cancellationToken) is IConditionalOperation ifOperation)
            {
                operation = ifOperation.Condition;
                return true;
            }
        }

        operation = null!;
        return false;
    }

    private readonly record struct ConditionKey(SyntaxTree Tree, int Start, int Length);

    private sealed class PredicateConditionGroup(ExpressionSyntax syntax)
    {
        public ExpressionSyntax Syntax { get; } = syntax;
        public List<OperationId> Lowered { get; } = [];
    }

    /// <summary>
    /// Projects configuration companion facts from the same control-flow graph and operation traversal
    /// that produced the accepted behavior input. Exact Microsoft <c>ConfigurationBinder
    /// .GetValue&lt;bool&gt;</c> reads project through the authoritative-symbol admission; an admitted
    /// read that flows through exactly one compiler-bound local assigned once from the read directly
    /// into an <c>if</c> boolean condition also projects a condition fact anchored to the real
    /// source-backed condition operation. Reassigned locals, locals that flow through helper calls,
    /// and several consuming conditions fail closed and keep only the read. The same invocation can
    /// never be admitted twice because each CFG operation belongs to exactly one block.
    /// </summary>
    private static void CollectConfigurationSemanticFacts(
        ControlFlowGraph cfg,
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        StableProjectId project,
        RoslynConfigurationSemanticFactCollector configurationFacts)
    {
        var admittedReads = new List<AdmittedConfigurationReadDraft>();
        foreach (var block in cfg.Blocks.OrderBy(block => block.Ordinal))
        {
            foreach (var operation in block.Operations.SelectMany(EnumerateSelfAndChildren))
            {
                AdmitConfigurationRead(operation, methodId, operationById, documents, methodEvidence, project, configurationFacts, admittedReads);
            }

            if (block.BranchValue is not null)
            {
                foreach (var operation in EnumerateSelfAndChildren(block.BranchValue))
                {
                    AdmitConfigurationRead(operation, methodId, operationById, documents, methodEvidence, project, configurationFacts, admittedReads);
                }
            }
        }

        foreach (var read in admittedReads)
        {
            if (TryResolveDirectLocalCondition(read.Call, cfg, operationById, out var conditionId, out var conditionOperation))
            {
                // The condition fact retains the canonical union of the admitted read evidence and the
                // exact if-condition operation evidence. Duplicate evidence identities are collapsed
                // and the deterministic order is by evidence identity so the payload never depends on
                // enumeration order.
                configurationFacts.AddCondition(
                    methodId,
                    read.OperationId,
                    conditionId,
                    CombineEvidence(
                        ResolveEvidence(read.Call, documents, methodEvidence),
                        ResolveEvidence(conditionOperation, documents, methodEvidence)));
            }
        }
    }

    /// <summary>
    /// Combines two evidence sets into one canonical, deterministic union: duplicates are collapsed
    /// by evidence identity and the surviving entries are ordered by evidence identity ordinal. The
    /// caller keeps the combined evidence with the derived fact so certainty can never exceed the
    /// weakest contributor.
    /// </summary>
    private static ImmutableArray<EvidenceRef> CombineEvidence(
        ImmutableArray<EvidenceRef> first,
        ImmutableArray<EvidenceRef> second)
    {
        if (first.IsEmpty)
        {
            return second;
        }

        if (second.IsEmpty)
        {
            return first;
        }

        return first
            .AddRange(second)
            .DistinctBy(item => item.Id.Value, StringComparer.Ordinal)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void AdmitConfigurationRead(
        IOperation operation,
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        StableProjectId project,
        RoslynConfigurationSemanticFactCollector configurationFacts,
        List<AdmittedConfigurationReadDraft> admittedReads)
    {
        if (operation is not IInvocationOperation call
            || !operationById.TryGetValue(call, out var invocationId))
        {
            return;
        }

        if (configurationFacts.TryAdmitRead(
                project,
                methodId,
                call,
                invocationId,
                out _,
                out _,
                ResolveEvidence(call, documents, methodEvidence)))
        {
            admittedReads.Add(new AdmittedConfigurationReadDraft(call, invocationId));
        }
    }

    /// <summary>
    /// Resolves the direct local-to-<c>if</c> condition association for one admitted read. The read's
    /// value must flow through an assignment whose target is exactly one compiler-bound local, that
    /// local must be assigned exactly once in the method, and exactly one conditional branch must
    /// consume the local directly as its boolean condition. Any reassignment, helper-call flow, or
    /// multiple consuming conditions fails closed with no condition fact. The returned condition id is
    /// the exact flattened operation of the accepted behavior input and the returned operation is that
    /// same accepted branch-value operation so its own source evidence can join the condition fact.
    /// </summary>
    private static bool TryResolveDirectLocalCondition(
        IInvocationOperation call,
        ControlFlowGraph cfg,
        Dictionary<IOperation, OperationId> operationById,
        out OperationId conditionId,
        out IOperation conditionOperation)
    {
        conditionId = default;
        conditionOperation = null!;
        IOperation? current = call;
        while (current?.Parent is IConversionOperation { IsImplicit: true } conversion)
        {
            current = conversion;
        }

        if (current?.Parent is not ISimpleAssignmentOperation assignment
            || UnwrapImplicitConversions(assignment.Target) is not ILocalReferenceOperation targetLocal)
        {
            return false;
        }

        var local = targetLocal.Local;
        if (CountAssignmentsToLocal(cfg, local) != 1
            || HasByReferenceEscape(cfg, local))
        {
            return false;
        }

        OperationId? found = null;
        IOperation? foundOperation = null;
        foreach (var block in cfg.Blocks)
        {
            if (block.ConditionalSuccessor is null || block.BranchValue is null)
            {
                continue;
            }

            var value = UnwrapImplicitConversions(block.BranchValue);
            if (value is ILocalReferenceOperation conditionLocal
                && SymbolEqualityComparer.Default.Equals(conditionLocal.Local, local)
                && block.BranchValue.Syntax is { } conditionSyntax
                && IsIfStatementCondition(conditionSyntax)
                && operationById.TryGetValue(block.BranchValue, out var id))
            {
                if (found is not null)
                {
                    return false;
                }

                found = id;
                foundOperation = block.BranchValue;
            }
        }

        if (found is null || foundOperation is null)
        {
            return false;
        }

        conditionId = found.Value;
        conditionOperation = foundOperation;
        return true;
    }

    /// <summary>
    /// True when the syntax node is the condition expression of an actual <c>if</c> statement. While,
    /// do-while, for, and foreach conditions never admit a condition association even when they flow
    /// through the same direct local shape.
    /// </summary>
    private static bool IsIfStatementCondition(SyntaxNode syntax)
    {
        SyntaxNode? current = syntax;
        while (current is not null)
        {
            if (current.Parent is IfStatementSyntax ifStatement
                && ifStatement.Condition == current)
            {
                return true;
            }

            if (current.Parent is WhileStatementSyntax whileStatement
                && whileStatement.Condition == current)
            {
                return false;
            }

            if (current.Parent is DoStatementSyntax doStatement
                && doStatement.Condition == current)
            {
                return false;
            }

            if (current.Parent is ForStatementSyntax forStatement
                && forStatement.Condition == current)
            {
                return false;
            }

            if (current.Parent is ForEachStatementSyntax forEachStatement
                && forEachStatement.Expression == current)
            {
                return false;
            }

            current = current.Parent;
        }

        return false;
    }

    /// <summary>
    /// True when the local escapes by reference (ref/out/in argument) anywhere in the method. A
    /// by-reference escape is an unsupported write before association: the callee can mutate the
    /// local, so the direct single-write condition shape fails closed. The <c>ref</c>/<c>out</c>/<c>in</c>
    /// keyword is carried on the argument's bound parameter, and the argument value is the local
    /// reference itself (optionally through implicit conversions), so no separate ref-expression
    /// operation is required.
    /// </summary>
    private static bool HasByReferenceEscape(ControlFlowGraph cfg, ILocalSymbol local)
    {
        foreach (var operation in EnumerateOperations(cfg.OriginalOperation))
        {
            if (operation is not IArgumentOperation argument
                || argument.Parameter?.RefKind is not (RefKind.Ref or RefKind.Out or RefKind.In))
            {
                continue;
            }

            if (UnwrapImplicitConversions(argument.Value) is ILocalReferenceOperation reference
                && SymbolEqualityComparer.Default.Equals(reference.Local, local))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Counts every write to the local in the method body: the declaration initializer (represented
    /// as <see cref="IVariableDeclaratorOperation"/> with a non-null initializer, not as a separate
    /// assignment operation) plus every simple or compound assignment whose target is the local. The
    /// direct condition shape requires exactly one write, so a later reassignment of a declared local
    /// fails closed exactly like a read-assignment followed by another write.
    /// </summary>
    private static int CountAssignmentsToLocal(ControlFlowGraph cfg, ILocalSymbol local)
    {
        int count = 0;
        foreach (var operation in EnumerateOperations(cfg.OriginalOperation))
        {
            switch (operation)
            {
                case IVariableDeclaratorOperation declarator
                    when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local)
                    && declarator.Initializer is not null:
                    count++;
                    break;
                case ISimpleAssignmentOperation simple
                    when UnwrapImplicitConversions(simple.Target) is ILocalReferenceOperation reference
                    && SymbolEqualityComparer.Default.Equals(reference.Local, local):
                    count++;
                    break;
                case ICompoundAssignmentOperation compound
                    when UnwrapImplicitConversions(compound.Target) is ILocalReferenceOperation reference
                    && SymbolEqualityComparer.Default.Equals(reference.Local, local):
                    count++;
                    break;
            }
        }

        return count;
    }

    private sealed record AdmittedConfigurationReadDraft(
        IInvocationOperation Call,
        OperationId OperationId);

    /// <summary>
    /// Projects compiler-proven status-switch arms from every switch statement in the method body. A
    /// switch is admitted only when its value is a status-typed enum member access and EVERY arm
    /// reaches exactly one distinct admitted ASP.NET Core outcome helper; any ambiguous or unsupported
    /// arm fails the whole switch closed with no fact (architecture decision). No Method Flow switch edge is added
    /// or reinterpreted.
    /// </summary>
    private static void CollectStatusSwitchFacts(
        ControlFlowGraph cfg,
        IMethodSymbol actionMethod,
        StableProjectId project,
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        RoslynNonGetSemanticFactCollector nonGetFacts,
        CancellationToken cancellationToken)
    {
        var bodySyntax = cfg.OriginalOperation.Syntax;
        if (bodySyntax is null || !models.TryGetValue(bodySyntax.SyntaxTree, out var model))
        {
            return;
        }

        // The action method symbol is threaded from the accepted body extraction so CreatedAtAction
        // target resolution stays compiler-bound; re-resolving from syntax is never authoritative.
        // The span index binds body-tree helper calls to the flattened operation identities so the
        // scenario join pairs each arm to its exact outcome operation.
        var spanToId = BuildSpanOperationIndex(operationById);
        foreach (var switchSyntax in bodySyntax.DescendantNodes().OfType<SwitchStatementSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (model.GetOperation(switchSyntax, cancellationToken) is not ISwitchOperation switchOperation)
            {
                continue;
            }

            var value = UnwrapImplicitConversions(switchOperation.Value);
            if (value is not IPropertyReferenceOperation statusProperty
                || statusProperty.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            {
                continue;
            }

            var statusEnumType = enumType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
            // The switch statement is not a Method Flow edge or node; when the accepted operation
            // traversal did not flatten the switch itself, a stable span-anchored identity anchors the
            // companion fact without altering the accepted Method Flow.
            var switchId = operationById.TryGetValue(switchOperation, out var flattenedSwitchId)
                ? flattenedSwitchId
                : RoslynBehaviorExtractor.CreateOperationId(switchOperation, methodId, "Switch", 0, 0, 0, documents);

            // Phase 1: analyze every arm. Any arm with an unresolved status constant, zero or several
            // distinct helpers, or an unresolvable CreatedAtAction target fails the whole switch so
            // the status-to-outcome mapping is never presented as exact while one arm is not.
            var analyzedArms = new List<SwitchArmDraft>();
            bool switchFailed = false;
            foreach (var caseOperation in switchOperation.Cases)
            {
                string? memberName = ResolveCaseMemberName(caseOperation);
                if (memberName is null)
                {
                    switchFailed = true;
                    break;
                }

                // The case label (for example `case WidgetResultStatus.NotFound:`) is the arm's
                // status-to-outcome mapping anchor; the helper invocation evidence joins it below.
                var caseLabelSyntax = caseOperation.Clauses.FirstOrDefault()?.Syntax;
                var draft = AnalyzeSwitchArm(
                    caseOperation.Body,
                    caseLabelSyntax,
                    memberName,
                    statusEnumType,
                    switchId,
                    nonGetFacts,
                    project,
                    methodId,
                    operationById,
                    spanToId,
                    documents,
                    methodEvidence,
                    actionMethod);
                if (draft is null)
                {
                    switchFailed = true;
                    break;
                }

                analyzedArms.Add(draft);
            }

            if (switchFailed)
            {
                continue;
            }

            // Phase 2: the whole switch mapping is exact; emit every arm.
            foreach (var draft in analyzedArms)
            {
                nonGetFacts.AddStatusSwitchArm(
                    draft.MethodId,
                    draft.SwitchOperation,
                    draft.StatusEnumType,
                    draft.StatusMemberName,
                    draft.HelperKind,
                    draft.OutcomeOperation,
                    draft.CreatedActionName,
                    draft.CreatedTargetMethod,
                    draft.Evidence);
            }
        }

        // Switch expressions (for example `result.Status switch { ... }`) are admitted exactly like
        // switch statements: an enum-typed value, arms with exact status constants, and exactly one
        // distinct ASP.NET Core outcome helper per arm; any ambiguous arm fails the whole expression.
        foreach (var switchExpressionSyntax in bodySyntax.DescendantNodes().OfType<SwitchExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (model.GetOperation(switchExpressionSyntax, cancellationToken) is not ISwitchExpressionOperation switchExpression)
            {
                continue;
            }

            var switchValue = UnwrapImplicitConversions(switchExpression.Value);
            if (switchValue is not IPropertyReferenceOperation statusProperty
                || statusProperty.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            {
                continue;
            }

            var statusEnumType = enumType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
            // Switch expressions are not Method Flow edges or nodes; when the accepted traversal did
            // not flatten the expression, a stable span-anchored identity anchors the companion fact.
            var switchId = operationById.TryGetValue(switchExpression, out var flattenedSwitchId)
                ? flattenedSwitchId
                : RoslynBehaviorExtractor.CreateOperationId(switchExpression, methodId, "Switch", 0, 0, 0, documents);

            var analyzedArms = new List<SwitchArmDraft>();
            bool switchFailed = false;
            foreach (var arm in switchExpression.Arms)
            {
                string? memberName = ResolveExpressionArmMemberName(arm);
                if (memberName is null)
                {
                    switchFailed = true;
                    break;
                }

                // The arm pattern (for example `WidgetResultStatus.NotFound` or `_`) is the arm's
                // status-to-outcome mapping anchor; the helper invocation evidence joins it below.
                var draft = AnalyzeSwitchArm(
                    [arm.Value],
                    arm.Pattern?.Syntax,
                    memberName,
                    statusEnumType,
                    switchId,
                    nonGetFacts,
                    project,
                    methodId,
                    operationById,
                    spanToId,
                    documents,
                    methodEvidence,
                    actionMethod);
                if (draft is null)
                {
                    switchFailed = true;
                    break;
                }

                analyzedArms.Add(draft);
            }

            if (switchFailed)
            {
                continue;
            }

            foreach (var draft in analyzedArms)
            {
                nonGetFacts.AddStatusSwitchArm(
                    draft.MethodId,
                    draft.SwitchOperation,
                    draft.StatusEnumType,
                    draft.StatusMemberName,
                    draft.HelperKind,
                    draft.OutcomeOperation,
                    draft.CreatedActionName,
                    draft.CreatedTargetMethod,
                    draft.Evidence);
            }
        }
    }

    /// <summary>
    /// Projects direct terminal outcomes: admitted ASP.NET Core outcome helper invocations reached
    /// OUTSIDE every switch arm body of a method that already carries at least one admitted status
    /// switch arm (for example a success-path CreatedAtAction return after an admitted failure switch).
    /// The companion fact is keyed by the exact compiler-bound invocation operation and never
    /// synthesizes a status member; an invocation inside ANY switch arm body (admitted or not) is
    /// skipped so a failed or ambiguous arm can never leak a terminal claim. Methods without admitted
    /// status arms (ordinary Get) produce no direct terminal facts, keeping the accepted
    /// structural-result join authoritative.
    /// </summary>
    private static void CollectDirectTerminalOutcomeFacts(
        ControlFlowGraph cfg,
        IMethodSymbol actionMethod,
        StableProjectId project,
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        RoslynNonGetSemanticFactCollector nonGetFacts,
        CancellationToken cancellationToken)
    {
        if (!nonGetFacts.HasAdmittedStatusSwitchArm(methodId))
        {
            return;
        }

        var bodySyntax = cfg.OriginalOperation.Syntax;
        if (bodySyntax is null || !models.TryGetValue(bodySyntax.SyntaxTree, out _))
        {
            return;
        }

        var spanToId = BuildSpanOperationIndex(operationById);
        foreach (var operation in EnumerateOperations(cfg.OriginalOperation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation is not IInvocationOperation invocation
                || !TryMatchOutcomeHelper(nonGetFacts, project, invocation, out var helperKind)
                || IsInsideSwitchArm(invocation.Syntax)
                || !FlowsDirectlyToTerminalReturn(invocation))
            {
                continue;
            }

            var outcomeId = ResolveOutcomeOperationId(invocation, operationById, spanToId);
            if (outcomeId is null)
            {
                // A helper the accepted traversal never flattened is not compiler-bound and cannot
                // anchor an exact terminal fact; it fails closed rather than guessing an identity.
                continue;
            }

            string? createdActionName = null;
            MethodId? createdTargetMethod = null;
            if (helperKind == HttpOutcomeHelperKind.CreatedAtAction)
            {
                createdActionName = ResolveCreatedActionName(invocation);
                createdTargetMethod = ResolveCreatedTargetMethod(invocation, actionMethod, project);
                if (createdActionName is null || createdTargetMethod is null)
                {
                    continue;
                }
            }

            nonGetFacts.AddDirectTerminalOutcome(
                methodId,
                outcomeId.Value,
                helperKind,
                createdActionName,
                createdTargetMethod,
                ResolveEvidence(invocation, documents, methodEvidence));
        }
    }

    /// <summary>
    /// True when the node is syntactically inside any switch arm body (a switch statement section or
    /// a switch expression arm). Direct terminals are defined as helpers outside EVERY arm body, so
    /// arms that failed closed still prevent their helpers from becoming terminal claims.
    /// </summary>
    private static bool IsInsideSwitchArm(SyntaxNode? node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is SwitchSectionSyntax or SwitchExpressionArmSyntax)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the helper invocation's value flows directly to a method return through the
    /// operation-parent chain (implicit conversions only). Discarded expression statements, assigned
    /// locals, nested arguments, and superseded values never qualify, so an unused or intermediate
    /// helper call can never become a user-facing direct terminal outcome. This is operation-parent
    /// return provenance, never syntax-text matching.
    /// </summary>
    private static bool FlowsDirectlyToTerminalReturn(IOperation operation)
    {
        // Climb from the helper invocation through implicit conversions to its direct consumer.
        IOperation? current = operation;
        while (current?.Parent is IConversionOperation { IsImplicit: true } conversion)
        {
            current = conversion;
        }

        if (current?.Parent is not IReturnOperation returnOperation)
        {
            return false;
        }

        // The returned value must resolve back to the exact helper invocation through implicit
        // conversions only; a nested call, local reference, or explicit cast never qualifies.
        return returnOperation.ReturnedValue is { } returnedValue
            && ReferenceEquals(UnwrapImplicitConversions(returnedValue), operation);
    }

    /// <summary>
    /// Analyzes one switch arm and returns its exact fact draft, or null when the arm reaches zero or
    /// several distinct admitted helper invocations or an unresolvable CreatedAtAction target. The
    /// case label/pattern evidence (the status-to-outcome mapping) is combined with the helper
    /// invocation evidence so a status arm is never backed only by the helper call.
    /// </summary>
    private static SwitchArmDraft? AnalyzeSwitchArm(
        ImmutableArray<IOperation> body,
        SyntaxNode? caseLabelSyntax,
        string memberName,
        string statusEnumType,
        OperationId switchId,
        RoslynNonGetSemanticFactCollector nonGetFacts,
        StableProjectId project,
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById,
        Dictionary<(SyntaxTree Tree, int Start, int Length, string Kind), OperationId> spanToId,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        IMethodSymbol actionMethod)
    {
        var caseEvidence = ResolveSyntaxEvidence(
            caseLabelSyntax,
            $"status-arm:{memberName}",
            documents);
        var matching = body
            .SelectMany(EnumerateOperations)
            .OfType<IInvocationOperation>()
            .Where(invocation => TryMatchOutcomeHelper(nonGetFacts, project, invocation, out _))
            .ToArray();
        // Only helpers with a canonical flattened invocation identity are exact; a helper the
        // accepted traversal never flattened is not compiler-bound and fails the arm closed.
        var resolved = matching
            .Select(invocation => (Invocation: invocation, OutcomeId: ResolveOutcomeOperationId(invocation, operationById, spanToId)))
            .Where(item => item.OutcomeId is not null)
            .ToArray();
        var distinct = resolved
            .GroupBy(item => item.OutcomeId!.Value.Value, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (distinct.Length != 1)
        {
            // Zero admitted helpers (unsupported arm) or several distinct helpers (ambiguous arm)
            // never produce a fact; the first helper is never selected silently.
            return null;
        }

        var (invocation, outcomeId) = distinct[0];
        if (!TryMatchOutcomeHelper(nonGetFacts, project, invocation, out var helperKind))
        {
            return null;
        }

        string? createdActionName = null;
        MethodId? createdTargetMethod = null;
        if (helperKind == HttpOutcomeHelperKind.CreatedAtAction)
        {
            createdActionName = ResolveCreatedActionName(invocation);
            createdTargetMethod = ResolveCreatedTargetMethod(invocation, actionMethod, project);
            if (createdActionName is null || createdTargetMethod is null)
            {
                return null;
            }
        }

        var evidence = CombineEvidence(
            caseEvidence,
            ResolveEvidence(invocation, documents, methodEvidence));
        return new SwitchArmDraft(
            methodId,
            switchId,
            statusEnumType,
            memberName,
            helperKind,
            outcomeId!.Value,
            createdActionName,
            createdTargetMethod,
            evidence);
    }

    /// <summary>
    /// Resolves the exact flattened invocation identity of a switch-arm outcome helper. The id must
    /// be compiler-bound (the accepted traversal's instance map or its stable source-span index);
    /// there is no synthetic fallback id for helpers the accepted traversal never flattened.
    /// </summary>
    private static OperationId? ResolveOutcomeOperationId(
        IInvocationOperation invocation,
        Dictionary<IOperation, OperationId> operationById,
        Dictionary<(SyntaxTree Tree, int Start, int Length, string Kind), OperationId> spanToId)
        => TryResolveCompilerBoundOperationId(invocation, operationById, spanToId, out var id) ? id : null;

    private sealed record SwitchArmDraft(
        MethodId MethodId,
        OperationId SwitchOperation,
        string StatusEnumType,
        string StatusMemberName,
        HttpOutcomeHelperKind HelperKind,
        OperationId OutcomeOperation,
        string? CreatedActionName,
        MethodId? CreatedTargetMethod,
        ImmutableArray<EvidenceRef> Evidence);

    private static string? ResolveExpressionArmMemberName(ISwitchExpressionArmOperation arm)
    {
        if (arm.Pattern is IDiscardPatternOperation)
        {
            return "default";
        }

        if (arm.Pattern is IConstantPatternOperation constantPattern)
        {
            var value = UnwrapImplicitConversions(constantPattern.Value);
            if (value is IFieldReferenceOperation field)
            {
                return field.Field.Name;
            }

            if (value.ConstantValue is { HasValue: true } constant)
            {
                return Convert.ToString(constant.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        return null;
    }

    /// <summary>
    /// Matches one invocation to an exact ASP.NET Core ControllerBase outcome helper resolved from the
    /// same compilation, using the authoritative boundary symbol and the closed helper-name vocabulary.
    /// Lookalike containing types and unadmitted helper names never match.
    /// </summary>
    private static bool TryMatchOutcomeHelper(
        RoslynNonGetSemanticFactCollector nonGetFacts,
        StableProjectId project,
        IInvocationOperation invocation,
        out HttpOutcomeHelperKind helperKind)
    {
        helperKind = default;
        var target = invocation.TargetMethod;
        if (target is null
            || target.ContainingType is null
            || !RoslynNonGetSemanticFactCollector.OutcomeHelperNames.TryGetValue(target.Name, out var kind))
        {
            return false;
        }

        var containingType = target.ContainingType;
        if (!nonGetFacts.TryResolveControllerBase(project, out var controllerBase)
            || controllerBase is null
            || !SymbolEqualityComparer.Default.Equals(containingType.OriginalDefinition, controllerBase))
        {
            return false;
        }

        helperKind = kind;
        return true;
    }

    private static string? ResolveCaseMemberName(ISwitchCaseOperation caseOperation)
    {
        foreach (var clause in caseOperation.Clauses)
        {
            if (clause.CaseKind == CaseKind.Default)
            {
                return "default";
            }

            if (clause is ISingleValueCaseClauseOperation single)
            {
                var constant = UnwrapImplicitConversions(single.Value);
                if (constant is IFieldReferenceOperation field)
                {
                    return field.Field.Name;
                }

                if (constant.ConstantValue is { HasValue: true } value)
                {
                    return Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                }
            }
        }

        return null;
    }

    private static string? ResolveCreatedActionName(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is null || argument.Parameter.Ordinal != 0)
            {
                continue;
            }

            var value = UnwrapImplicitConversions(argument.Value);
            if (value.ConstantValue is { HasValue: true, Value: string actionName } && actionName.Length > 0)
            {
                return actionName;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the compiler-bound target controller method identity of a CreatedAtAction call. With
    /// no explicit controller name the target is a same-controller action; with a controller name the
    /// named type is resolved and must exist. The target method must be exactly one non-generic
    /// method with the exact action name; overloads, missing controllers, or missing methods fail
    /// closed with null so the scenario join never guesses by action-name text alone.
    /// </summary>
    private static MethodId? ResolveCreatedTargetMethod(
        IInvocationOperation invocation,
        IMethodSymbol actionMethod,
        StableProjectId project)
    {
        string? actionName = ResolveCreatedActionName(invocation);
        if (actionName is null)
        {
            return null;
        }

        INamedTypeSymbol? controllerType = null;
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is null || argument.Parameter.Ordinal != 1)
            {
                continue;
            }

            var value = UnwrapImplicitConversions(argument.Value);
            if (value.ConstantValue is { HasValue: true, Value: string controllerName } && controllerName.Length > 0)
            {
                controllerType = ResolveNamedType(actionMethod.ContainingType, controllerName);
                break;
            }
        }

        controllerType ??= actionMethod.ContainingType;
        if (controllerType is null)
        {
            return null;
        }

        var targets = controllerType.GetMembers(actionName)
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary
                && method.Arity == 0
                && !method.IsImplicitlyDeclared)
            .ToArray();
        if (targets.Length != 1)
        {
            return null;
        }

        return StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
            targets[0],
            project));
    }

    /// <summary>
    /// Resolves a controller type by its exact simple or fully qualified name. When the simple name
    /// is ambiguous across namespaces, no type is returned and the CreatedAtAction link fails closed.
    /// </summary>
    private static INamedTypeSymbol? ResolveNamedType(INamedTypeSymbol sameControllerType, string name)
    {
        var candidates = sameControllerType.ContainingAssembly.GlobalNamespace
            .GetNamespaceMembers()
            .SelectMany(EnumerateNamespaceTypes)
            .Where(type => string.Equals(type.MetadataName, name, StringComparison.Ordinal))
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;

        static IEnumerable<INamedTypeSymbol> EnumerateNamespaceTypes(INamespaceSymbol namespaceSymbol)
        {
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                yield return type;
            }

            foreach (var child in namespaceSymbol.GetNamespaceMembers())
            {
                foreach (var nested in EnumerateNamespaceTypes(child))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>
    /// Creates source evidence for one syntax node (for example a switch case label or arm pattern).
    /// The evidence anchors the status-to-outcome mapping independently of the helper invocation so a
    /// status arm is never backed only by the helper call.
    /// </summary>
    private static ImmutableArray<EvidenceRef> ResolveSyntaxEvidence(
        SyntaxNode? syntax,
        string symbol,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
    {
        if (syntax is null
            || !documents.TryGetValue(syntax.SyntaxTree, out var context)
            || syntax.Span.Length <= 0)
        {
            return [];
        }

        return ImmutableArray.Create(RoslynProgramIndexExtractor.CreateSourceEvidence(
            context.Document.Id,
            context.Document.LogicalPath,
            context.Text,
            syntax.Span,
            symbol,
            context.Document.Origin == SeqDoc.Core.ProgramIndex.DocumentOrigin.GeneratedSource));
    }

    private static ImmutableArray<EvidenceRef> CombineEvidence(
        params ImmutableArray<EvidenceRef>[] sources)
        => sources
            .SelectMany(source => source)
            .Where(item => item is not null)
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();

    /// <summary>
    /// Projects evidenced source observations (TODO/NOTE comments) from the method body. Observations
    /// are explicitly non-interaction: they never become scenario interactions, diagram messages, or
    /// behavioral edges, and they always retain conservative certainty.
    /// </summary>
    private static void CollectSourceObservationFacts(
        ControlFlowGraph cfg,
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        RoslynNonGetSemanticFactCollector nonGetFacts)
    {
        var bodySyntax = cfg.OriginalOperation.Syntax;
        if (bodySyntax is null || !models.TryGetValue(bodySyntax.SyntaxTree, out var model))
        {
            return;
        }

        foreach (var trivia in bodySyntax.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.Span.Length <= 0 || (trivia.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia) is false
                && trivia.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineCommentTrivia) is false
                && trivia.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineDocumentationCommentTrivia) is false))
            {
                continue;
            }

            var text = NormalizeCommentText(trivia);
            SourceObservationKind kind = DetectObservationKind(text);
            if (kind == SourceObservationKind.Unknown)
            {
                continue;
            }

            var anchorOperation = ResolveObservationAnchor(
                trivia,
                model,
                operationById,
                methodId,
                documents);
            if (anchorOperation is null)
            {
                continue;
            }

            if (!documents.TryGetValue(bodySyntax.SyntaxTree, out var context))
            {
                continue;
            }

            var evidence = ImmutableArray.Create(RoslynProgramIndexExtractor.CreateSourceEvidence(
                context.Document.Id,
                context.Document.LogicalPath,
                context.Text,
                trivia.Span,
                "comment",
                context.Document.Origin == SeqDoc.Core.ProgramIndex.DocumentOrigin.GeneratedSource));
            nonGetFacts.AddSourceObservation(
                methodId,
                anchorOperation.Value,
                kind,
                text,
                evidence);
        }
    }

    private static string NormalizeCommentText(SyntaxTrivia trivia)
    {
        var text = trivia.ToFullString();
        if (text.StartsWith("///", StringComparison.Ordinal))
        {
            text = text[3..];
        }
        else if (text.StartsWith("//", StringComparison.Ordinal))
        {
            text = text[2..];
        }
        else if (text.StartsWith("/*", StringComparison.Ordinal))
        {
            text = text.TrimStart('/');
        }

        return text.Trim().TrimEnd('*', '/').Trim();
    }

    private static SourceObservationKind DetectObservationKind(string text)
    {
        if (text.StartsWith("TODO", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("FIXME", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("HACK", StringComparison.OrdinalIgnoreCase))
        {
            return SourceObservationKind.Todo;
        }

        if (text.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase))
        {
            return SourceObservationKind.Note;
        }

        return SourceObservationKind.Unknown;
    }

    private static OperationId? ResolveObservationAnchor(
        SyntaxTrivia trivia,
        SemanticModel model,
        Dictionary<IOperation, OperationId> operationById,
        MethodId methodId,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
    {
        var token = trivia.Token;
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (model.GetOperation(node) is not { } operation)
            {
                continue;
            }

            if (operationById.TryGetValue(operation, out var id))
            {
                return id;
            }

            // Comments often attach to terminals (for example a return statement) whose operation the
            // accepted traversal never flattened. A stable span-anchored identity anchors the
            // companion observation without altering the accepted Method Flow.
            if (!operation.IsImplicit && operation.Syntax is not null)
            {
                return RoslynBehaviorExtractor.CreateOperationId(
                    operation,
                    methodId,
                    "ObservationAnchor",
                    0,
                    0,
                    0,
                    documents);
            }
        }

        return null;
    }

    /// <summary>
    /// Collects comparison semantic facts from lambda bodies. The control-flow graph hides lambda
    /// bodies inside flow anonymous function operations, so lambdas are enumerated from the method
    /// body syntax and resolved through the semantic model; this is the same syntax-to-operation path
    /// the EF query projector proves for navigation and predicate anchors. Comparison and operand
    /// operation ids are span-anchored with zero traversal ordinals so the EF query projector
    /// reproduces the exact same comparison anchor for the linked predicate; ids are deterministic
    /// per (method, kind, span).
    /// </summary>
    private static void CollectLambdaComparisonFacts(
        ControlFlowGraph cfg,
        MethodId methodId,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        RoslynSemanticFactCollector semanticFacts)
    {
        var bodySyntax = cfg.OriginalOperation.Syntax;
        if (bodySyntax is null)
        {
            return;
        }

        foreach (var lambda in bodySyntax.DescendantNodes().OfType<LambdaExpressionSyntax>())
        {
            if (!models.TryGetValue(lambda.SyntaxTree, out var model)
                || model.GetOperation(lambda) is not IAnonymousFunctionOperation anonymous)
            {
                continue;
            }

            foreach (var binary in EnumerateSelfAndChildren(anonymous.Body).OfType<IBinaryOperation>())
            {
                if (!IsSupportedComparison(binary))
                {
                    continue;
                }

                semanticFacts.AddComparison(
                    methodId,
                    CreateOperationId(binary, methodId, "Binary", 0, 0, 0, documents),
                    MapComparisonOperator(binary.OperatorKind),
                    CreateOperationId(binary.LeftOperand, methodId, MapKind(binary.LeftOperand).ToString(), 0, 0, 0, documents),
                    CreateOperationId(binary.RightOperand, methodId, MapKind(binary.RightOperand).ToString(), 0, 0, 0, documents),
                    ResolveEvidence(binary, documents, methodEvidence));
            }
        }
    }

    /// <summary>
    /// Resolves the returned value of a return-semantics block to a source-backed operation and its
    /// stable operation id. The compiler carries an explicit value return as the block's branch
    /// value. Blocks without a branch value (void returns, no-value returns) resolve to false so no
    /// provenance is invented.
    /// </summary>
    private static bool TryResolveReturnValue(
        BasicBlock block,
        Dictionary<IOperation, OperationId> operationById,
        out IOperation returnValue,
        out OperationId returnValueId)
    {
        var candidate = block.BranchValue;
        if (candidate is not null
            && IsSourceBacked(candidate)
            && operationById.TryGetValue(candidate, out var id))
        {
            returnValue = candidate;
            returnValueId = id;
            return true;
        }

        returnValue = null!;
        returnValueId = default;
        return false;
    }

    private static void CollectOperationSemanticFacts(
        IOperation root,
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        StableProjectId project,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        RoslynSemanticFactCollector semanticFacts,
        FrameworkAnalysisRequestCollector frameworkRequest,
        RoslynDependencyInjectionFactCollector dependencyInjectionFacts,
        RoslynStructuralResultFactCollector structuralResultFacts,
        RoslynNonGetSemanticFactCollector nonGetFacts,
        CompilationProfileId profileId,
        IReadOnlyDictionary<ILocalSymbol, IOperation> localInitializers,
        CancellationToken cancellationToken)
    {
        foreach (var operation in EnumerateSelfAndChildren(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (operation)
            {
                case IBinaryOperation binary when IsSupportedComparison(binary)
                    && operationById.TryGetValue(operation, out var comparisonId)
                    && operationById.TryGetValue(binary.LeftOperand, out var leftOperandId)
                    && operationById.TryGetValue(binary.RightOperand, out var rightOperandId):
                    semanticFacts.AddComparison(
                        methodId,
                        comparisonId,
                        MapComparisonOperator(binary.OperatorKind),
                        leftOperandId,
                        rightOperandId,
                        ResolveEvidence(operation, documents, methodEvidence));
                    break;
                case IBinaryOperation binary when IsDateTimeComparison(binary)
                    && operationById.TryGetValue(operation, out var timeId)
                    && operationById.TryGetValue(binary.LeftOperand, out var timeLeftId)
                    && TryResolveRightOperation(binary, operationById, out var timeRightId):
                    nonGetFacts.AddRelationalTimeFact(
                        methodId,
                        timeId,
                        RelationalTimeFactKind.TimeComparison,
                        MapComparisonOperator(binary.OperatorKind),
                        timeLeftId,
                        timeRightId,
                        ResolveConstantThreshold(binary.RightOperand),
                        ResolveEvidence(operation, documents, methodEvidence));
                    break;
                case IInvocationOperation call:
                    CollectArgumentBindings(
                        call.Arguments,
                        methodId,
                        call.TargetMethod,
                        operationById,
                        documents,
                        methodEvidence,
                        project,
                        projectsByAssembly,
                        semanticFacts);
                    if (operationById.TryGetValue(operation, out var invocationId))
                    {
                        var operationEvidence = ResolveEvidence(operation, documents, methodEvidence);
                        frameworkRequest.AddOperation(FrameworkAnalysisRequestProjector.ProjectOperationDescriptor(
                            call,
                            methodId,
                            invocationId,
                            operationEvidence,
                            operationById,
                            documents,
                            models,
                            project,
                            profileId,
                            localInitializers: localInitializers,
                            hostChainProof: frameworkRequest.HostChainProof,
                            dispatchCancellationToken: cancellationToken));
                        dependencyInjectionFacts.AddRegistration(project, methodId, call, invocationId, operationEvidence);
                        structuralResultFacts.AddFactoryCall(
                            methodId,
                            call,
                            invocationId,
                            operationById,
                            operationEvidence,
                            models);
                    }
                    else
                    {
                        // Loop bodies and other blocks the accepted traversal did not flatten still
                        // expose exact EF mutations; a stable span-anchored identity anchors the
                        // companion fact without altering the accepted Method Flow.
                        invocationId = RoslynBehaviorExtractor.CreateOperationId(
                            call,
                            methodId,
                            "Invocation",
                            0,
                            0,
                            0,
                            documents);
                    }

                    break;
                case IObjectCreationOperation creation when creation.Constructor is not null:
                    CollectArgumentBindings(
                        creation.Arguments,
                        methodId,
                        creation.Constructor,
                        operationById,
                        documents,
                        methodEvidence,
                        project,
                        projectsByAssembly,
                        semanticFacts);
                    if (operationById.TryGetValue(operation, out var creationId))
                    {
                        frameworkRequest.AddOperation(FrameworkAnalysisRequestProjector.ProjectOperationDescriptor(
                            creation,
                            methodId,
                            creationId,
                            ResolveEvidence(operation, documents, methodEvidence),
                            documents,
                            project,
                            profileId));
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Projects relational patterns (for example <c>quantity is &lt;= 0</c>) from the operation tree.
    /// The pattern input and threshold are exact compiler operands; the resulting fact is conservative.
    /// </summary>
    private static void CollectRelationalPatterns(
        IOperation root,
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        RoslynNonGetSemanticFactCollector nonGetFacts)
    {
        var bodySyntax = root.Syntax;
        if (bodySyntax is null || !models.TryGetValue(bodySyntax.SyntaxTree, out var model))
        {
            return;
        }

        foreach (var isPattern in bodySyntax.DescendantNodes().OfType<IsPatternExpressionSyntax>())
        {
            if (model.GetOperation(isPattern) is not IIsPatternOperation isPatternOperation
                || isPatternOperation.Pattern is not IRelationalPatternOperation relational)
            {
                continue;
            }

            var operatorKind = MapPatternOperator(relational.OperatorKind);
            if (operatorKind is null)
            {
                continue;
            }

            var patternId = operationById.TryGetValue(relational, out var flattenedId)
                ? flattenedId
                : RoslynBehaviorExtractor.CreateOperationId(relational, methodId, "RelationalPattern", 0, 0, 0, documents);
            var leftId = operationById.TryGetValue(isPatternOperation.Value, out var flattenedLeft)
                ? flattenedLeft
                : RoslynBehaviorExtractor.CreateOperationId(isPatternOperation.Value, methodId, "PatternInput", 0, 0, 0, documents);
            var threshold = relational.Value.ConstantValue is { HasValue: true } constant
                ? Convert.ToString(constant.Value, CultureInfo.InvariantCulture)
                : null;
            nonGetFacts.AddRelationalTimeFact(
                methodId,
                patternId,
                RelationalTimeFactKind.RelationalPattern,
                operatorKind.Value,
                leftId,
                null,
                threshold,
                ResolveEvidence(isPatternOperation, documents, methodEvidence));
        }
    }

    /// <summary>
    /// Records state assignments, EF query terminals, and EF mutations from one full-body source-order
    /// walk so every source-ordered companion fact shares one authoritative ordinal. The body tree
    /// always contains every statement, including assignments and call sites inside loops that the CFG
    /// block traversal omits, so each fact joins exactly once and interleaved operations keep their
    /// compiler source order. Operations already flattened by the accepted traversal reuse their stable
    /// identity; every other operation gets a stable span-anchored identity without altering the
    /// accepted Method Flow. A reference guard keeps the projection deterministic even if a future
    /// Roslyn version shares nodes.
    /// </summary>
    private static void CollectSourceOrderedOperationFacts(
        IOperation bodyOperation,
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        RoslynNonGetSemanticFactCollector nonGetFacts)
    {
        // The accepted traversal keys operationById by the control-flow-graph operation instances;
        // the body tree can expose distinct instances for the same source span. A stable span index
        // maps the compiler-proven flattened ids back onto body-tree operations without inventing
        // identities or duplicating facts.
        var spanToId = BuildSpanOperationIndex(operationById);

        var visited = new HashSet<IOperation>(ReferenceEqualityComparer.Instance);
        foreach (var operation in EnumerateOperationsInSourceOrder(bodyOperation))
        {
            if (!visited.Add(operation))
            {
                continue;
            }

            switch (operation)
            {
                case ISimpleAssignmentOperation assignment
                    when TryResolveStateAssignment(assignment, operationById, out var targetMember, out var targetType, out var valueKind, out var value)
                    && TryResolveCompilerBoundOperationId(assignment, operationById, spanToId, out var assignmentId):
                    // State assignments are admitted only when the accepted traversal flattened the
                    // assignment; loop-body and object-initializer assignments the CFG omitted never
                    // invent a fact. The shared source-order ordinal still interleaves the admitted
                    // assignments with queries and mutations authoritatively.
                    nonGetFacts.AddStateAssignment(
                        methodId,
                        assignmentId,
                        targetMember,
                        targetType,
                        valueKind,
                        value,
                        ResolveEvidence(assignment, documents, methodEvidence));
                    break;
                case IInvocationOperation call:
                    if (RoslynNonGetSemanticFactCollector.TryMatchQueryTerminal(call.TargetMethod, out _))
                    {
                        var queryId = TryResolveCompilerBoundOperationId(call, operationById, spanToId, out var flattenedQueryId)
                            ? flattenedQueryId
                            : RoslynBehaviorExtractor.CreateOperationId(call, methodId, "Invocation", 0, 0, 0, documents);
                        nonGetFacts.AddEfQueryTerminal(methodId, queryId);
                        break;
                    }

                    if (RoslynNonGetSemanticFactCollector.TryMatchMutation(call, out var kind, out var dbContextType, out var entityType, out var mutationTargetMember))
                    {
                        var invocationId = TryResolveCompilerBoundOperationId(call, operationById, spanToId, out var flattenedId)
                            ? flattenedId
                            : RoslynBehaviorExtractor.CreateOperationId(call, methodId, "Mutation", 0, 0, 0, documents);
                        var argumentOperation = ResolveMutationArgumentOperation(call, operationById);
                        nonGetFacts.AddEfMutation(
                            methodId,
                            invocationId,
                            kind,
                            dbContextType,
                            entityType,
                            argumentOperation,
                            mutationTargetMember,
                            ResolveEvidence(call, documents, methodEvidence));
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Builds the stable source-span index over the accepted traversal's operation identities. The
    /// control-flow graph and the body tree can expose distinct operation instances for one source
    /// span; this index lets companion collection bind body-tree operations to the exact compiler-
    /// proven flattened identities without inventing or duplicating ids. The operation kind is part of
    /// the key so a same-span implicit conversion can never shadow the invocation identity it wraps.
    /// Only source-backed operations are indexed so synthesized CFG plumbing never claims a source
    /// anchor.
    /// </summary>
    private static Dictionary<(SyntaxTree Tree, int Start, int Length, string Kind), OperationId> BuildSpanOperationIndex(
        Dictionary<IOperation, OperationId> operationById)
    {
        var spanToId = new Dictionary<(SyntaxTree Tree, int Start, int Length, string Kind), OperationId>();
        foreach ((IOperation operation, OperationId id) in operationById)
        {
            if (IsSourceBacked(operation))
            {
                spanToId.TryAdd(
                    (operation.Syntax!.SyntaxTree, operation.Syntax.SpanStart, operation.Syntax.Span.Length, MapKind(operation).ToString()),
                    id);
            }
        }

        return spanToId;
    }

    /// <summary>
    /// Resolves the compiler-proven operation id for a body-tree operation. The id is taken from the
    /// accepted traversal's instance map when available, then from the stable source-span index so a
    /// distinct body-tree instance still binds to the exact flattened identity. Operations the
    /// accepted traversal never flattened are not compiler-bound and return false.
    /// </summary>
    private static bool TryResolveCompilerBoundOperationId(
        IOperation operation,
        Dictionary<IOperation, OperationId> operationById,
        Dictionary<(SyntaxTree Tree, int Start, int Length, string Kind), OperationId> spanToId,
        out OperationId id)
    {
        if (operationById.TryGetValue(operation, out id))
        {
            return true;
        }

        if (operation.Syntax is { } syntax
            && syntax.Span.Length > 0
            && spanToId.TryGetValue(
                (syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length, MapKind(operation).ToString()),
                out id))
        {
            return true;
        }

        id = default;
        return false;
    }

    private static OperationId? ResolveMutationArgumentOperation(
        IInvocationOperation call,
        Dictionary<IOperation, OperationId> operationById)
    {
        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter is null || argument.Parameter.Ordinal != 0)
            {
                continue;
            }

            var value = UnwrapImplicitConversions(argument.Value);
            if (value.IsImplicit || value.Syntax is null)
            {
                continue;
            }

            return operationById.TryGetValue(value, out var id) ? id : null;
        }

        return null;
    }

    private static bool TryResolveStateAssignment(
        ISimpleAssignmentOperation assignment,
        Dictionary<IOperation, OperationId> operationById,
        out string targetMember,
        out string targetType,
        out StateAssignmentValueKind valueKind,
        out string value)
    {
        targetMember = string.Empty;
        targetType = string.Empty;
        valueKind = StateAssignmentValueKind.Unknown;
        value = string.Empty;
        if (assignment.IsImplicit || !IsSourceBacked(assignment))
        {
            return false;
        }

        var target = UnwrapImplicitConversions(assignment.Target);
        string? targetTypeName = target.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
        switch (target)
        {
            case IPropertyReferenceOperation property:
                targetMember = $"{RoslynProgramIndexExtractor.GetMetadataName(property.Property.ContainingType)}.{property.Property.Name}";
                targetTypeName ??= property.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
                break;
            case IFieldReferenceOperation field:
                targetMember = $"{RoslynProgramIndexExtractor.GetMetadataName(field.Field.ContainingType)}.{field.Field.Name}";
                targetTypeName ??= field.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
                break;
            default:
                return false;
        }

        if (string.IsNullOrWhiteSpace(targetMember) || string.IsNullOrWhiteSpace(targetTypeName))
        {
            return false;
        }

        var assigned = UnwrapImplicitConversions(assignment.Value);
        switch (assigned)
        {
            case IFieldReferenceOperation enumField when enumField.Field.ContainingType.TypeKind == TypeKind.Enum:
                valueKind = StateAssignmentValueKind.EnumConstant;
                value = enumField.Field.Name;
                break;
            case ILiteralOperation literal when literal.ConstantValue is { HasValue: true } constant:
                valueKind = StateAssignmentValueKind.Literal;
                value = Convert.ToString(constant.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                break;
            case IParameterReferenceOperation parameter:
                valueKind = StateAssignmentValueKind.Parameter;
                value = parameter.Parameter.Name;
                break;
            case ILocalReferenceOperation local:
                valueKind = StateAssignmentValueKind.Local;
                value = local.Local.Name;
                break;
            case IObjectCreationOperation creation:
                valueKind = StateAssignmentValueKind.ObjectCreation;
                value = creation.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) ?? string.Empty;
                break;
            default:
                return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        targetType = targetTypeName;
        return true;
    }

    private static bool IsDateTimeComparison(IBinaryOperation binary)
        => !binary.IsImplicit
            && IsSourceBacked(binary)
            && IsComparisonKind(binary.OperatorKind)
            && (binary.LeftOperand.Type?.SpecialType == SpecialType.System_DateTime
                || binary.RightOperand.Type?.SpecialType == SpecialType.System_DateTime);

    private static bool TryResolveRightOperation(
        IBinaryOperation binary,
        Dictionary<IOperation, OperationId> operationById,
        out OperationId? rightId)
    {
        rightId = null;
        return operationById.TryGetValue(binary.RightOperand, out var id) ? (rightId = id, true).Item2 : false;
    }

    private static string? ResolveConstantThreshold(IOperation operand)
        => operand.ConstantValue is { HasValue: true } constant
            ? Convert.ToString(constant.Value, CultureInfo.InvariantCulture)
            : null;

    private static ComparisonOperatorKind? MapPatternOperator(BinaryOperatorKind kind) => kind switch
    {
        BinaryOperatorKind.Equals => ComparisonOperatorKind.Equal,
        BinaryOperatorKind.NotEquals => ComparisonOperatorKind.NotEqual,
        BinaryOperatorKind.LessThan => ComparisonOperatorKind.LessThan,
        BinaryOperatorKind.LessThanOrEqual => ComparisonOperatorKind.LessThanOrEqual,
        BinaryOperatorKind.GreaterThan => ComparisonOperatorKind.GreaterThan,
        BinaryOperatorKind.GreaterThanOrEqual => ComparisonOperatorKind.GreaterThanOrEqual,
        _ => null,
    };

    private static void CollectArgumentBindings(
        ImmutableArray<IArgumentOperation> arguments,
        MethodId methodId,
        IMethodSymbol target,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence,
        StableProjectId project,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        RoslynSemanticFactCollector semanticFacts)
    {
        if (arguments.IsDefault)
        {
            return;
        }

        var targetId = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
            target,
            ResolveProject(target, project, projectsByAssembly)));
        foreach (var argument in arguments)
        {
            // Compiler-provided parameter ordinals are authoritative; synthesized default-value
            // arguments carry implicit values and are deliberately excluded.
            if (argument.Parameter is null
                || argument.Value is null
                || argument.Value.IsImplicit
                || !IsSourceBacked(argument.Value)
                || !operationById.TryGetValue(argument.Value, out var argumentOperationId))
            {
                continue;
            }

            semanticFacts.AddArgumentBinding(
                methodId,
                targetId,
                argument.Parameter.Ordinal,
                argumentOperationId,
                ResolveEvidence(argument.Value, documents, methodEvidence));
        }
    }

    private static bool IsSupportedComparison(IBinaryOperation binary) =>
        binary.OperatorMethod is null
        && !binary.IsImplicit
        && IsSourceBacked(binary)
        && IsComparisonKind(binary.OperatorKind);

    private static bool IsComparisonKind(BinaryOperatorKind kind) => kind switch
    {
        BinaryOperatorKind.Equals or
        BinaryOperatorKind.NotEquals or
        BinaryOperatorKind.LessThan or
        BinaryOperatorKind.LessThanOrEqual or
        BinaryOperatorKind.GreaterThan or
        BinaryOperatorKind.GreaterThanOrEqual => true,
        _ => false,
    };

    private static ComparisonOperatorKind MapComparisonOperator(BinaryOperatorKind kind) => kind switch
    {
        BinaryOperatorKind.Equals => ComparisonOperatorKind.Equal,
        BinaryOperatorKind.NotEquals => ComparisonOperatorKind.NotEqual,
        BinaryOperatorKind.LessThan => ComparisonOperatorKind.LessThan,
        BinaryOperatorKind.LessThanOrEqual => ComparisonOperatorKind.LessThanOrEqual,
        BinaryOperatorKind.GreaterThan => ComparisonOperatorKind.GreaterThan,
        BinaryOperatorKind.GreaterThanOrEqual => ComparisonOperatorKind.GreaterThanOrEqual,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "Unsupported comparison operator kind."),
    };

    private static ExtractedBlockTerminalKind ResolveTerminal(BasicBlock block)
    {
        if (block.Kind == BasicBlockKind.Exit)
        {
            return ExtractedBlockTerminalKind.Exit;
        }

        if (block.ConditionalSuccessor is not null)
        {
            return ExtractedBlockTerminalKind.Conditional;
        }

        if (block.FallThroughSuccessor is { } fallThrough)
        {
            return fallThrough.Semantics switch
            {
                ControlFlowBranchSemantics.Return => ExtractedBlockTerminalKind.Return,
                ControlFlowBranchSemantics.Throw => ExtractedBlockTerminalKind.Throw,
                ControlFlowBranchSemantics.Rethrow => ExtractedBlockTerminalKind.Rethrow,
                ControlFlowBranchSemantics.ProgramTermination => ExtractedBlockTerminalKind.Exit,
                _ => ExtractedBlockTerminalKind.None,
            };
        }

        return ExtractedBlockTerminalKind.None;
    }

    /// <summary>
    /// Maps each user-written throw statement to its control-flow block and the static type of the
    /// exception expression. The exception expression is the IThrowOperation operand with implicit
    /// conversions removed; the block is matched by the expression's syntax span because Roslyn does
    /// not share operation instances between the body tree and the control-flow graph. Compiler-
    /// synthesized throws (for example the exhaustive-check throw of a switch expression) have no
    /// IThrowOperation and are deliberately absent from the result.
    /// </summary>
    private static Dictionary<int, ITypeSymbol> ComputeRealThrows(
        IMethodBodyOperation bodyOperation,
        ControlFlowGraph cfg)
    {
        var realThrows = new Dictionary<int, ITypeSymbol>();
        foreach (var throwOperation in EnumerateOperations(bodyOperation).OfType<IThrowOperation>())
        {
            if (throwOperation.Exception is null)
            {
                continue;
            }

            IOperation exceptionExpression = UnwrapImplicitConversions(throwOperation.Exception);
            if (exceptionExpression.Syntax is null || exceptionExpression.Type is null)
            {
                continue;
            }

            foreach (var block in cfg.Blocks.Where(block => block.BranchValue is not null))
            {
                if (!EnumerateSelfAndChildren(block.BranchValue!).Any(candidate =>
                        candidate.Syntax is { } syntax
                        && syntax.SpanStart == exceptionExpression.Syntax.SpanStart
                        && syntax.Span.Length == exceptionExpression.Syntax.Span.Length
                        && ReferenceEquals(syntax.SyntaxTree, exceptionExpression.Syntax.SyntaxTree)))
                {
                    continue;
                }

                realThrows[block.Ordinal] = exceptionExpression.Type;
                break;
            }
        }

        return realThrows;
    }

    private static IOperation UnwrapImplicitConversions(IOperation operation)
    {
        IOperation current = operation;
        while (current is IConversionOperation { IsImplicit: true } conversion)
        {
            current = conversion.Operand;
        }

        return current;
    }

    private static IEnumerable<IOperation> EnumerateOperations(IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.TryPop(out var operation))
        {
            yield return operation;
            foreach (var child in operation.ChildOperations)
            {
                pending.Push(child);
            }
        }
    }

    /// <summary>
    /// Depth-first pre-order enumeration that preserves source order. The accepted traversal uses a
    /// stack (children visited in reverse) because order is irrelevant there; the EF mutation walk
    /// requires authoritative statement order, so it pushes children in reverse to pop them in order.
    /// </summary>
    private static IEnumerable<IOperation> EnumerateOperationsInSourceOrder(IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.TryPop(out var operation))
        {
            yield return operation;
            foreach (var child in operation.ChildOperations.Reverse())
            {
                pending.Push(child);
            }
        }
    }

    private static IEnumerable<IOperation> EnumerateSelfAndChildren(IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.TryPop(out var operation))
        {
            yield return operation;
            foreach (var child in operation.ChildOperations)
            {
                pending.Push(child);
            }
        }
    }

    private static IEnumerable<ILoopOperation> EnumerateLoopOperations(IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.TryPop(out var operation))
        {
            if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                continue;
            }

            if (operation is ILoopOperation loop)
            {
                yield return loop;
            }

            foreach (var child in operation.ChildOperations)
            {
                pending.Push(child);
            }
        }
    }

    private static bool IsCaughtByEnclosingTry(
        int blockOrdinal,
        ControlFlowRegion root,
        ITypeSymbol? thrownType,
        ControlFlowRegion? excludedRegion)
    {
        foreach (var tryRegion in EnumerateRegions(root)
                     .Where(region => region.Kind is ControlFlowRegionKind.Try
                         or ControlFlowRegionKind.TryAndCatch
                         or ControlFlowRegionKind.TryAndFinally)
                     .Where(region => blockOrdinal >= region.FirstBlockOrdinal && blockOrdinal <= region.LastBlockOrdinal)
                     .Where(region => !ReferenceEquals(region, excludedRegion))
                     .OrderByDescending(region => region.FirstBlockOrdinal))
        {
            foreach (var catchRegion in tryRegion.NestedRegions)
            {
                if (catchRegion.Kind is not ControlFlowRegionKind.Catch)
                {
                    continue;
                }

                if (CanCatch(thrownType, catchRegion.ExceptionType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CanCatch(ITypeSymbol? thrownType, ITypeSymbol? catchType)
    {
        if (catchType is null)
        {
            return true;
        }

        if (thrownType is null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(thrownType, catchType)
            || IsAssignableTo(thrownType, catchType);
    }

    private static bool IsAssignableTo(ITypeSymbol source, ITypeSymbol destination)
    {
        if (destination.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        switch (source)
        {
            case INamedTypeSymbol sourceNamed when destination is INamedTypeSymbol destinationNamed:
                INamedTypeSymbol? current = sourceNamed;
                while (current is not null)
                {
                    if (SymbolEqualityComparer.Default.Equals(current, destinationNamed)
                        || current.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, destinationNamed)))
                    {
                        return true;
                    }

                    current = current.BaseType;
                }

                return false;

            case ITypeParameterSymbol typeParameter:
                return typeParameter.ConstraintTypes.Any(constraint => IsAssignableTo(constraint, destination));

            default:
                return false;
        }
    }

    private static ControlFlowRegion? FindEnclosingCatch(int blockOrdinal, ControlFlowRegion root) =>
        EnumerateRegions(root)
            .Where(region => region.Kind is ControlFlowRegionKind.Catch or ControlFlowRegionKind.FilterAndHandler)
            .Where(region => blockOrdinal >= region.FirstBlockOrdinal && blockOrdinal <= region.LastBlockOrdinal)
            .OrderByDescending(region => region.FirstBlockOrdinal)
            .FirstOrDefault();

    private static IEnumerable<ControlFlowRegion> EnumerateRegions(ControlFlowRegion root)
    {
        var pending = new Stack<ControlFlowRegion>();
        pending.Push(root);
        while (pending.TryPop(out var region))
        {
            yield return region;
            foreach (var nested in region.NestedRegions)
            {
                pending.Push(nested);
            }
        }
    }

    private static OperationId FlattenOperation(
        IOperation operation,
        OperationId? parent,
        MethodId methodId,
        int blockOrdinal,
        ref int evaluationOrdinal,
        Dictionary<(string Kind, int BlockOrdinal), int> siblingOrdinals,
        Dictionary<IOperation, OperationId> operationById,
        ImmutableArray<ExtractedOperation>.Builder operations,
        ImmutableArray<TypeInstantiationFact>.Builder instantiations,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        StableProjectId project,
        ImmutableArray<EvidenceRef> methodEvidence,
        IReadOnlyDictionary<ILocalSymbol, IOperation> localInitializers)
    {
        if (operationById.TryGetValue(operation, out var existing))
        {
            return existing;
        }

        var kind = MapKind(operation);
        var siblingKey = (kind.ToString(), blockOrdinal);
        var siblingOrdinal = siblingOrdinals.GetValueOrDefault(siblingKey);
        siblingOrdinals[siblingKey] = siblingOrdinal + 1;

        var operationId = CreateOperationId(operation, methodId, kind.ToString(), blockOrdinal, evaluationOrdinal, siblingOrdinal, documents);
        operationById.Add(operation, operationId);

        var operandBuilder = ImmutableArray.CreateBuilder<OperationId>();
        foreach (var child in operation.ChildOperations)
        {
            operandBuilder.Add(FlattenOperation(
                child,
                operationId,
                methodId,
                blockOrdinal,
                ref evaluationOrdinal,
                siblingOrdinals,
                operationById,
                operations,
                instantiations,
                documents,
                projectsByAssembly,
                project,
                methodEvidence,
                localInitializers));
        }

        var referencedMethods = ImmutableArray<MethodId>.Empty;
        var referencedTypes = ImmutableArray<SymbolId>.Empty;
        var referencedMembers = ImmutableArray<SymbolId>.Empty;
        string? localName = null;
        int? parameterOrdinal = null;
        ExtractedInvocationPayload? invocation = null;
        ExtractedAssignmentPayload? assignment = null;
        ExtractedConversionPayload? conversion = null;
        ExtractedAwaitPayload? awaitPayload = null;
        ExtractedReturnPayload? returnPayload = null;
        ExtractedThrowPayload? throwPayload = null;

        switch (operation)
        {
            case ILocalReferenceOperation localReference:
                localName = localReference.Local.Name;
                break;
            case IParameterReferenceOperation parameterReference:
                parameterOrdinal = parameterReference.Parameter.Ordinal;
                break;
            case IInvocationOperation call:
                var target = call.TargetMethod;
                var targetId = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
                    target,
                    ResolveProject(target, project, projectsByAssembly)));
                referencedMethods = [targetId];
                referencedTypes = ImmutableArray.Create(CreateSymbolId(target.ContainingType, project, projectsByAssembly));
                var argumentMappings = call.Arguments
                    .Where(argument => !argument.IsImplicit)
                    .Select(argument => new ExtractedInvocationArgument(
                        operationById.GetValueOrDefault(argument.Value),
                        argument.Parameter?.Ordinal,
                        argument.Parameter is not null && operationById.ContainsKey(argument.Value)))
                    .OrderBy(argument => argument.ParameterOrdinal ?? int.MaxValue)
                    .ToImmutableArray();
                invocation = new ExtractedInvocationPayload(
                    targetId,
                    IsDispatchable(target),
                    target.MethodKind == MethodKind.DelegateInvoke,
                    target.IsStatic,
                    target.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor,
                    IsDynamic: false,
                    call.Arguments
                        .Select(argument => argument.Value)
                        .Where(value => operationById.ContainsKey(value))
                        .Select(value => operationById[value])
                        .ToImmutableArray(),
                     RoslynProgramIndexExtractor.GetMetadataName(target.ContainingType, documents),
                     target.Name,
                     IsInsideNestedFunction(operation),
                     projectsByAssembly.ContainsKey(target.ContainingAssembly),
                     target.ContainingAssembly.Identity.Name,
                      IsPlatformAssembly(target.ContainingAssembly.Identity.Name),
                      argumentMappings);
                break;
            case IDynamicInvocationOperation dynamicCall:
                invocation = new ExtractedInvocationPayload(
                    Target: null,
                    IsDispatchable: false,
                    IsDelegateOrEventInvoke: false,
                    IsStatic: false,
                    IsConstructor: false,
                    IsDynamic: true,
                    dynamicCall.Arguments
                        .Select(argument => argument is IArgumentOperation argumentOperation ? argumentOperation.Value : argument)
                        .Where(value => operationById.ContainsKey(value))
                        .Select(value => operationById[value])
                        .ToImmutableArray());
                break;
            case IEventAssignmentOperation eventAssignment:
                if (eventAssignment.EventReference is IEventReferenceOperation eventReference)
                {
                    var accessor = eventAssignment.Adds ? eventReference.Event.AddMethod : eventReference.Event.RemoveMethod;
                    if (accessor is { } accessorMethod)
                    {
                        var accessorId = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
                            accessorMethod,
                            ResolveProject(accessorMethod, project, projectsByAssembly)));
                        referencedMethods = [accessorId];
                        invocation = new ExtractedInvocationPayload(
                            accessorId,
                            IsDispatchable: false,
                            IsDelegateOrEventInvoke: true,
                            IsStatic: false,
                            IsConstructor: false,
                            IsDynamic: false,
                            eventAssignment.HandlerValue is { } handler && operationById.TryGetValue(handler, out var handlerId)
                                ? ImmutableArray.Create(handlerId)
                                : [],
                             RoslynProgramIndexExtractor.GetMetadataName(accessorMethod.ContainingType, documents),
                             accessorMethod.Name,
                             IsInsideNestedFunction(operation),
                             projectsByAssembly.ContainsKey(accessorMethod.ContainingAssembly),
                             accessorMethod.ContainingAssembly.Identity.Name,
                             IsPlatformAssembly(accessorMethod.ContainingAssembly.Identity.Name));
                    }
                }

                break;
            case IObjectCreationOperation creation when creation.Constructor is not null:
                var constructorId = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(
                    creation.Constructor,
                    ResolveProject(creation.Constructor, project, projectsByAssembly)));
                referencedMethods = [constructorId];
                if (creation.Type is not null)
                {
                    var createdType = CreateSymbolId(creation.Type, project, projectsByAssembly);
                    referencedTypes = [createdType];
                    instantiations.Add(new TypeInstantiationFact(
                        createdType,
                        methodId,
                        operationId,
                        ResolveEvidence(operation, documents, methodEvidence),
                        CertaintyLevel.Exact));
                }

                break;
            case ISimpleAssignmentOperation simpleAssignment:
                if (operationById.TryGetValue(simpleAssignment.Target, out var targetOp)
                    && operationById.TryGetValue(simpleAssignment.Value, out var valueOp))
                {
                    assignment = new ExtractedAssignmentPayload(targetOp, valueOp, IsCompound: false);
                }

                break;
            case ICompoundAssignmentOperation compoundAssignment:
                if (operationById.TryGetValue(compoundAssignment.Target, out var compoundTarget)
                    && operationById.TryGetValue(compoundAssignment.Value, out var compoundValue))
                {
                    assignment = new ExtractedAssignmentPayload(compoundTarget, compoundValue, IsCompound: true);
                }

                break;
            case IConversionOperation conversionOperation:
                conversion = new ExtractedConversionPayload(
                    conversionOperation.Operand.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) ?? string.Empty,
                    conversionOperation.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) ?? string.Empty);
                break;
            case IAwaitOperation awaitOperation:
                if (operationById.TryGetValue(awaitOperation.Operation, out var awaited))
                {
                    awaitPayload = new ExtractedAwaitPayload(awaited);
                }

                break;
            case IReturnOperation returnOperation:
                if (returnOperation.ReturnedValue is not null
                    && operationById.TryGetValue(returnOperation.ReturnedValue, out var returned))
                {
                    returnPayload = new ExtractedReturnPayload(returned);
                }
                else
                {
                    returnPayload = new ExtractedReturnPayload(null);
                }

                break;
            case IThrowOperation throwOperation:
                if (throwOperation.Exception is not null
                    && operationById.TryGetValue(throwOperation.Exception, out var thrown))
                {
                    throwPayload = new ExtractedThrowPayload(thrown, IsRethrow: false);
                }
                else
                {
                    throwPayload = new ExtractedThrowPayload(null, IsRethrow: true);
                }

                break;
            case IFieldReferenceOperation fieldReference:
                referencedMembers = [CreateSymbolId(fieldReference.Field, project, projectsByAssembly)];
                break;
            case IPropertyReferenceOperation propertyReference:
                referencedMembers = [CreateSymbolId(propertyReference.Property, project, projectsByAssembly)];
                break;
        }

        operations.Add(new ExtractedOperation(
            operationId,
            methodId,
            kind,
            parent,
            operandBuilder.ToImmutable(),
            evaluationOrdinal,
            operation.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) ?? string.Empty,
            operation.ConstantValue.HasValue
                 ? operation.ConstantValue.Value is null
                     ? null
                     : Convert.ToString(operation.ConstantValue.Value, CultureInfo.InvariantCulture) ?? string.Empty
                : null,
            operation.IsImplicit,
            IsSourceBacked(operation),
            referencedMethods,
            referencedTypes,
            referencedMembers,
            invocation,
            assignment,
            conversion,
            awaitPayload,
            returnPayload,
            throwPayload,
            localName,
            parameterOrdinal,
            ResolveEvidence(operation, documents, methodEvidence),
             CertaintyLevel.Exact,
             operation.ConstantValue.HasValue));
        evaluationOrdinal++;
        return operationId;
    }

    private static Dictionary<ILocalSymbol, bool> CollectLocals(IOperation root)
    {
        var locals = new Dictionary<ILocalSymbol, bool>(SymbolEqualityComparer.Default);
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.TryPop(out var operation))
        {
            if (operation is ILocalReferenceOperation localReference)
            {
                locals[localReference.Local] = true;
            }

            foreach (var child in operation.ChildOperations)
            {
                pending.Push(child);
            }
        }

        return locals;
    }

    private static void VisitRegion(
        ControlFlowRegion region,
        FlowRegionId? parent,
        ImmutableArray<ExtractedExceptionRegion>.Builder regions,
        Dictionary<ControlFlowRegion, FlowRegionId> regionById,
        ref int ordinal,
        MethodId methodId,
        ImmutableArray<EvidenceRef> evidence)
    {
        var id = StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(
            methodId,
            region.Kind.ToString(),
            ordinal));
        regionById.Add(region, id);
        var currentOrdinal = ordinal;
        ordinal++;
        regions.Add(new ExtractedExceptionRegion(
            id,
            MapRegionKind(region.Kind),
            parent,
            currentOrdinal,
            region.FirstBlockOrdinal,
            region.LastBlockOrdinal,
            region.ExceptionType?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
            evidence,
            CertaintyLevel.Exact));

        foreach (var nested in region.NestedRegions.OrderBy(nested => nested.FirstBlockOrdinal).ThenBy(nested => nested.Kind))
        {
            VisitRegion(nested, id, regions, regionById, ref ordinal, methodId, evidence);
        }
    }

    private static void CollectTypeHierarchy(
        LoadedProject project,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly,
        ImmutableArray<ExtractedTypeNode>.Builder typeNodes,
        CancellationToken cancellationToken)
    {
        foreach (var type in EnumerateSourceTypes(project.Compilation, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var typeId = CreateSymbolId(type, project.StableId, projectsByAssembly);
            SymbolId? baseType = type.BaseType is null || type.BaseType.SpecialType == SpecialType.System_Object
                ? null
                : CreateSymbolId(type.BaseType, project.StableId, projectsByAssembly);
            var interfaces = type.Interfaces
                .Select(item => CreateSymbolId(item, project.StableId, projectsByAssembly))
                .Distinct()
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToImmutableArray();
            typeNodes.Add(new ExtractedTypeNode(
                typeId,
                project.StableId,
                RoslynProgramIndexExtractor.GetMetadataName(type),
                baseType,
                interfaces,
                type.IsSealed,
                type.IsAbstract,
                type.TypeKind == TypeKind.Interface,
                IsSource: true,
                RoslynProgramIndexExtractor.CreateDeclarationEvidence(type, documents)));
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateSourceTypes(Compilation compilation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var type in EnumerateNamespaceTypes(compilation.Assembly.GlobalNamespace, cancellationToken))
        {
            if (type.Locations.Any(location => location.IsInSource))
            {
                yield return type;
            }
        }

        static IEnumerable<INamedTypeSymbol> EnumerateNamespaceTypes(INamespaceSymbol namespaceSymbol, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                foreach (var nested in EnumerateNested(type, cancellationToken))
                {
                    yield return nested;
                }
            }

            foreach (var child in namespaceSymbol.GetNamespaceMembers())
            {
                foreach (var nested in EnumerateNamespaceTypes(child, cancellationToken))
                {
                    yield return nested;
                }
            }
        }

        static IEnumerable<INamedTypeSymbol> EnumerateNested(INamedTypeSymbol type, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return type;
            foreach (var child in type.GetTypeMembers())
            {
                foreach (var nested in EnumerateNested(child, cancellationToken))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>
    /// Maps one operation to its accepted closed vocabulary kind. Internal so the callback boundary
    /// collector can build companion operation maps with exactly the same kind strings as the
    /// accepted behavior extraction (accepted contract); no behavior output or fingerprint changes.
    /// </summary>
    internal static ExtractedOperationKind MapKind(IOperation operation) => operation switch
    {
        IBlockOperation => ExtractedOperationKind.Block,
        IExpressionStatementOperation => ExtractedOperationKind.ExpressionStatement,
        IVariableDeclaratorOperation => ExtractedOperationKind.VariableDeclaration,
        ILocalReferenceOperation => ExtractedOperationKind.LocalReference,
        IParameterReferenceOperation => ExtractedOperationKind.ParameterReference,
        IFieldReferenceOperation => ExtractedOperationKind.FieldReference,
        IPropertyReferenceOperation => ExtractedOperationKind.PropertyReference,
        IMemberReferenceOperation => ExtractedOperationKind.MemberReference,
        IArrayElementReferenceOperation => ExtractedOperationKind.ArrayElementReference,
        ILiteralOperation => ExtractedOperationKind.Literal,
        IInvocationOperation => ExtractedOperationKind.Invocation,
        IDynamicInvocationOperation => ExtractedOperationKind.DynamicInvocation,
        IEventAssignmentOperation => ExtractedOperationKind.EventAssignment,
        IObjectCreationOperation => ExtractedOperationKind.ObjectCreation,
        IDelegateCreationOperation => ExtractedOperationKind.DelegateCreation,
        IAnonymousFunctionOperation => ExtractedOperationKind.AnonymousFunction,
        ISimpleAssignmentOperation => ExtractedOperationKind.Assignment,
        ICompoundAssignmentOperation => ExtractedOperationKind.CompoundAssignment,
        IIncrementOrDecrementOperation => ExtractedOperationKind.IncrementOrDecrement,
        IConversionOperation => ExtractedOperationKind.Conversion,
        IBinaryOperation => ExtractedOperationKind.Binary,
        IUnaryOperation => ExtractedOperationKind.Unary,
        IConditionalOperation => ExtractedOperationKind.Conditional,
        ICoalesceOperation => ExtractedOperationKind.Coalesce,
        IReturnOperation => ExtractedOperationKind.Return,
        IThrowOperation => ExtractedOperationKind.Throw,
        IAwaitOperation => ExtractedOperationKind.Await,
        IWhileLoopOperation whileLoop => whileLoop.ConditionIsTop
            ? ExtractedOperationKind.WhileLoop
            : ExtractedOperationKind.DoWhileLoop,
        ILoopOperation loop => loop.LoopKind switch
        {
            LoopKind.For => ExtractedOperationKind.ForLoop,
            LoopKind.ForEach => ExtractedOperationKind.ForEachLoop,
            _ => ExtractedOperationKind.WhileLoop,
        },
        ILockOperation => ExtractedOperationKind.Lock,
        IUsingOperation => ExtractedOperationKind.Using,
        IEndOperation => ExtractedOperationKind.End,
        _ => ExtractedOperationKind.Unknown,
    };

    private static ExtractedRegionKind MapRegionKind(ControlFlowRegionKind kind) => kind switch
    {
        ControlFlowRegionKind.Root => ExtractedRegionKind.Root,
        ControlFlowRegionKind.Try => ExtractedRegionKind.Try,
        ControlFlowRegionKind.Catch => ExtractedRegionKind.Catch,
        ControlFlowRegionKind.Filter => ExtractedRegionKind.Filter,
        ControlFlowRegionKind.FilterAndHandler => ExtractedRegionKind.FilterAndHandler,
        ControlFlowRegionKind.Finally => ExtractedRegionKind.Finally,
        ControlFlowRegionKind.TryAndCatch => ExtractedRegionKind.TryAndCatch,
        ControlFlowRegionKind.TryAndFinally => ExtractedRegionKind.TryAndFinally,
        ControlFlowRegionKind.LocalLifetime => ExtractedRegionKind.LocalLifetime,
        ControlFlowRegionKind.StaticLocalInitializer => ExtractedRegionKind.StaticLocalInitializer,
        ControlFlowRegionKind.ErroneousBody => ExtractedRegionKind.ErroneousBody,
        _ => ExtractedRegionKind.Unknown,
    };

    private static bool IsDispatchable(IMethodSymbol target) =>
        target.MethodKind == MethodKind.DelegateInvoke
        || target.ContainingType.TypeKind == TypeKind.Interface
        || target.IsAbstract
        || (target.IsVirtual && !target.IsSealed && !target.ContainingType.IsSealed);

    private static bool IsSourceBacked(IOperation operation) =>
        operation.Syntax is not null
        && operation.Syntax.Span.Length > 0
        && operation.Syntax.SpanStart >= 0;

    internal static ImmutableArray<EvidenceRef> ResolveEvidence(
        IOperation operation,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> methodEvidence)
    {
        if (operation.Syntax is not null
            && documents.TryGetValue(operation.Syntax.SyntaxTree, out var context)
            && operation.Syntax.Span.Length > 0)
        {
            var kind = context.Document.Origin == SeqDoc.Core.ProgramIndex.DocumentOrigin.GeneratedSource
                ? EvidenceKind.GeneratedSource
                : EvidenceKind.Source;
            return ImmutableArray.Create(RoslynProgramIndexExtractor.CreateSourceEvidence(
                context.Document.Id,
                context.Document.LogicalPath,
                context.Text,
                operation.Syntax.Span,
                operation.Kind.ToString(),
                context.Document.Origin == SeqDoc.Core.ProgramIndex.DocumentOrigin.GeneratedSource));
        }

        return methodEvidence;
    }

    internal static OperationId CreateOperationId(
        IOperation operation,
        MethodId methodId,
        string kind,
        int blockOrdinal,
        int evaluationOrdinal,
        int siblingOrdinal,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
    {
        if (operation.Syntax is not null
            && documents.TryGetValue(operation.Syntax.SyntaxTree, out var context)
            && operation.Syntax.Span.Length > 0)
        {
            return StableIdentity.CreateBehaviorOperationId(new BehaviorOperationIdentityDescriptor(
                methodId,
                kind,
                blockOrdinal,
                evaluationOrdinal,
                context.Document.Id,
                operation.Syntax.SpanStart,
                operation.Syntax.Span.Length,
                siblingOrdinal));
        }

        return StableIdentity.CreateBehaviorOperationId(new BehaviorOperationIdentityDescriptor(
            methodId,
            kind,
            blockOrdinal,
            evaluationOrdinal,
            null,
            0,
            0,
            siblingOrdinal));
    }

    private static ImmutableArray<EvidenceRef> CreateMethodEvidence(
        IMethodSymbol method,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents) =>
        method.DeclaringSyntaxReferences
            .OrderBy(reference => RoslynProgramIndexExtractor.GetDocumentSortKey(reference.SyntaxTree, documents), StringComparer.Ordinal)
            .ThenBy(reference => reference.Span.Start)
            .SelectMany(reference =>
            {
                if (!documents.TryGetValue(reference.SyntaxTree, out var context))
                {
                    return [];
                }

                var kind = context.Document.Origin == SeqDoc.Core.ProgramIndex.DocumentOrigin.GeneratedSource
                    ? EvidenceKind.GeneratedSource
                    : EvidenceKind.Source;
                return ImmutableArray.Create(RoslynProgramIndexExtractor.CreateSourceEvidence(
                    context.Document.Id,
                    context.Document.LogicalPath,
                    context.Text,
                    reference.Span,
                    method.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
                    context.Document.Origin == SeqDoc.Core.ProgramIndex.DocumentOrigin.GeneratedSource));
            })
            .DistinctBy(item => item.Id)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();

    private static StableProjectId ResolveProject(
        ISymbol symbol,
        StableProjectId fallback,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly) =>
        projectsByAssembly.GetValueOrDefault(symbol.ContainingAssembly, fallback);

    private static bool IsPlatformAssembly(string? assemblyName)
        => assemblyName is "mscorlib" or "netstandard" or "System" or "Microsoft.CSharp"
            || assemblyName?.StartsWith("System.", StringComparison.Ordinal) == true;

    private static SymbolId CreateSymbolId(
        ISymbol symbol,
        StableProjectId fallback,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly) =>
        StableIdentity.CreateSymbolId(RoslynProgramIndexExtractor.CreateSymbolDescriptor(
            symbol,
            ResolveProject(symbol, fallback, projectsByAssembly)));
}
