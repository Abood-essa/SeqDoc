using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Frameworks;

/// <summary>
/// Projects compiler-shape facts only from Roslyn symbols into framework-neutral records. It never
/// decides ASP.NET Core controller/action eligibility; the modular framework model owns those rules.
/// Shapes are always exact because they come directly from compiler symbols; callers that cannot
/// supply a projected shape leave <see cref="SymbolDescriptor.MethodShape"/> null and the model fails
/// closed.
/// </summary>
internal static class FrameworkSymbolEligibilityProjector
{
    /// <summary>
    /// Projects the compiler shape of one method plus its declaring type, binding both to the exact
    /// indexed symbols through the same Program Index identity helpers. Returns null when the method
    /// has no usable containing type, which callers must treat as incomplete eligibility input.
    /// </summary>
    public static FrameworkMethodShape? ProjectMethodShape(IMethodSymbol method, StableProjectId project)
    {
        ArgumentNullException.ThrowIfNull(method);
        var declaringType = method.ContainingType;
        if (declaringType is null)
        {
            return null;
        }

        return new FrameworkMethodShape(
            StableIdentity.CreateSymbolId(RoslynProgramIndexExtractor.CreateMethodDescriptor(method, project)),
            RoslynProgramIndexExtractor.CreateSymbolId(declaringType, project),
            IsOrdinary: method.MethodKind == MethodKind.Ordinary,
            IsPublic: method.DeclaredAccessibility == Accessibility.Public,
            IsStatic: method.IsStatic,
            IsAbstract: method.IsAbstract,
            GenericArity: method.Arity,
            DeclaringType: ProjectTypeShape(declaringType),
            ImplementedInterfaceMembers: ProjectImplementedInterfaceMembers(method, declaringType, project),
            DeclaringTypeAttributes: ProjectAttributeIdentities(declaringType));
    }

    /// <summary>
    /// Projects the exact set of interface members <paramref name="method"/> implements, implicit and
    /// explicit. Implicit implementation is proven with
    /// <see cref="INamedTypeSymbol.FindImplementationForInterfaceMember"/> over every interface the
    /// declaring type carries (including inherited interfaces); explicit implementation is read
    /// directly from <see cref="IMethodSymbol.ExplicitInterfaceImplementations"/>. Only ordinary
    /// interface methods are considered; property/event accessors are out of scope for this projection.
    /// </summary>
    private static ImmutableArray<FrameworkInterfaceMemberIdentity> ProjectImplementedInterfaceMembers(
        IMethodSymbol method, INamedTypeSymbol declaringType, StableProjectId project)
    {
        var builder = ImmutableArray.CreateBuilder<FrameworkInterfaceMemberIdentity>();
        var seen = new HashSet<(SymbolId InterfaceType, SymbolId InterfaceMethod)>();

        foreach (var interfaceType in declaringType.AllInterfaces)
        {
            foreach (var interfaceMember in interfaceType.GetMembers().OfType<IMethodSymbol>())
            {
                if (interfaceMember.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                // FindImplementationForInterfaceMember resolves both implicit and explicit
                // implementations to the same method, so a single resolution proves the member is
                // implemented; ExplicitInterfaceImplementations then classifies which shape it is.
                var implementation = declaringType.FindImplementationForInterfaceMember(interfaceMember);
                if (implementation is null || !SymbolEqualityComparer.Default.Equals(implementation, method))
                {
                    continue;
                }

                var isExplicit = method.ExplicitInterfaceImplementations
                    .Any(explicitMember => SymbolEqualityComparer.Default.Equals(explicitMember, interfaceMember));
                AddInterfaceMember(builder, seen, interfaceType, interfaceMember, project, isExplicit);
            }
        }

        return builder
            .OrderBy(item => item.InterfaceType.AssemblyIdentity, StringComparer.Ordinal)
            .ThenBy(item => item.InterfaceType.MetadataName, StringComparer.Ordinal)
            .ThenBy(item => item.InterfaceMethodMetadataName, StringComparer.Ordinal)
            .ThenBy(item => item.GenericArity)
            .ThenBy(item => item.InterfaceMethodSymbol.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void AddInterfaceMember(
        ImmutableArray<FrameworkInterfaceMemberIdentity>.Builder builder,
        HashSet<(SymbolId InterfaceType, SymbolId InterfaceMethod)> seen,
        INamedTypeSymbol interfaceType,
        IMethodSymbol interfaceMethod,
        StableProjectId project,
        bool isExplicit)
    {
        var interfaceTypeSymbol = RoslynProgramIndexExtractor.CreateSymbolId(interfaceType, project);
        var interfaceMethodSymbol = StableIdentity.CreateSymbolId(
            RoslynProgramIndexExtractor.CreateMethodDescriptor(interfaceMethod, project));
        if (!seen.Add((interfaceTypeSymbol, interfaceMethodSymbol)))
        {
            // The same interface member can be reached through more than one AllInterfaces path (for
            // example diamond inheritance); the first proof is retained and duplicates are dropped.
            return;
        }

        builder.Add(new FrameworkInterfaceMemberIdentity(
            interfaceTypeSymbol,
            interfaceMethodSymbol,
            ProjectTypeIdentity(interfaceType),
            interfaceMethod.MetadataName,
            interfaceMethod.Arity,
            interfaceMethod.Parameters
                .Select(parameter => new ParameterIdentityDescriptor(
                    RoslynProgramIndexExtractor.ToParameterRefKind(parameter.RefKind),
                    DisplayType(parameter.Type)))
                .ToImmutableArray(),
            DisplayType(interfaceMethod.ReturnType),
            isExplicit,
            ProjectAttributeIdentities(interfaceType),
            ProjectAttributeIdentities(interfaceMethod)));
    }

    /// <summary>
    /// Projects the exact original attribute-class identity (assembly, assembly version, metadata name)
    /// of every attribute applied to <paramref name="symbol"/>, resolved from the compiler's own
    /// <see cref="AttributeData.AttributeClass"/> rather than a display-name string, so a model can
    /// reject a same-qualified-name attribute defined in a foreign assembly. Each attribute's
    /// <c>typeof(...)</c> constructor arguments are resolved the same way, in declaration order, for
    /// attributes whose meaning depends on a type argument (for example <c>[FaultContract(typeof(X))]</c>).
    /// </summary>
    private static ImmutableArray<FrameworkAttributeApplicationIdentity> ProjectAttributeIdentities(ISymbol symbol)
        => symbol.GetAttributes()
            .Where(attribute => attribute.AttributeClass is not null)
            .Select(attribute => new FrameworkAttributeApplicationIdentity(
                ProjectTypeIdentity(attribute.AttributeClass!),
                attribute.ConstructorArguments
                    .Where(argument => argument.Kind == TypedConstantKind.Type && argument.Value is INamedTypeSymbol)
                    .Select(argument => ProjectTypeIdentity((INamedTypeSymbol)argument.Value!))
                    .ToImmutableArray()))
            .ToImmutableArray();

    private static string DisplayType(ITypeSymbol type)
        => type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);

    /// <summary>
    /// Projects the compiler shape of one named type, including the exact base-type chain.
    /// </summary>
    public static FrameworkTypeShape ProjectTypeShape(INamedTypeSymbol type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new FrameworkTypeShape(
            ProjectTypeIdentity(type),
            IsClass: type.TypeKind == TypeKind.Class,
            IsPublicOrNestedPublic: IsPublicOrNestedPublic(type),
            IsAbstract: type.IsAbstract,
            IsStatic: type.IsStatic,
            GenericArity: type.Arity,
            BaseTypeChain: ProjectBaseTypeChain(type),
            Interfaces: type.AllInterfaces
                .Select(ProjectTypeIdentity)
                .OrderBy(identity => identity.AssemblyIdentity, StringComparer.Ordinal)
                .ThenBy(identity => identity.MetadataName, StringComparer.Ordinal)
                .ThenBy(identity => identity.AssemblyVersion, StringComparer.Ordinal)
                .ToImmutableArray());
    }

    /// <summary>
    /// Projects the exact named-type identity (assembly name, assembly version, metadata name) from
    /// compiler symbols.
    /// </summary>
    public static FrameworkTypeIdentity ProjectTypeIdentity(INamedTypeSymbol type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var assembly = type.ContainingAssembly;
        return new FrameworkTypeIdentity(
            assembly?.Identity.Name ?? string.Empty,
            assembly?.Identity.Version?.ToString() ?? string.Empty,
            RoslynProgramIndexExtractor.GetMetadataName(type));
    }

    /// <summary>
    /// Projects source evidence for a symbol using the same stable document/evidence helpers as the
    /// Program Index extractor. The repository root is required and must be a nonblank absolute
    /// checkout path; evidence references are canonicalized by repository-relative logical path then
    /// source span, and absolute checkout paths are never passed into document identities or evidence
    /// records. Empty file paths and files outside the repository fail closed.
    /// </summary>
    public static ImmutableArray<EvidenceRef> ProjectSourceEvidence(
        ISymbol symbol,
        StableProjectId project,
        string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var builder = ImmutableArray.CreateBuilder<EvidenceRef>();
        foreach (var item in symbol.DeclaringSyntaxReferences
                     .Select(reference => (Reference: reference, LogicalPath: ResolveRepositoryRelativePath(repositoryRoot, reference.SyntaxTree.FilePath)))
                     .OrderBy(item => item.LogicalPath, StringComparer.Ordinal)
                     .ThenBy(item => item.Reference.Span.Start))
        {
            var documentId = StableIdentity.CreateDocumentId(new DocumentIdentityDescriptor(
                project,
                DocumentIdentityKind.Source,
                item.LogicalPath));
            builder.Add(RoslynProgramIndexExtractor.CreateSourceEvidence(
                documentId,
                item.LogicalPath,
                item.Reference.SyntaxTree.GetText(),
                item.Reference.Span,
                symbol.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
                generated: false));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<FrameworkTypeIdentity> ProjectBaseTypeChain(INamedTypeSymbol type)
    {
        var builder = ImmutableArray.CreateBuilder<FrameworkTypeIdentity>();
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            builder.Add(ProjectTypeIdentity(current));
        }

        return builder.ToImmutable();
    }

    private static bool IsPublicOrNestedPublic(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves a checkout-independent repository-relative logical path for one source file. The
    /// path is canonicalized through <see cref="RepositoryRelativePath.Normalize"/> so evidence
    /// artifacts always use '/' on every platform. Empty or blank paths and rooted or escaping paths
    /// fail closed because evidence identity must never depend on the checkout path.
    /// </summary>
    private static string ResolveRepositoryRelativePath(string repositoryRoot, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException(
                "Cannot project source evidence for a symbol without a physical source file.");
        }

        try
        {
            return RepositoryRelativePath.Normalize(Path.GetRelativePath(repositoryRoot, filePath));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"Cannot project a repository-relative path for '{filePath}'.",
                exception);
        }
    }
}
