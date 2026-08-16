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
            DeclaringType: ProjectTypeShape(declaringType));
    }

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
            BaseTypeChain: ProjectBaseTypeChain(type));
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
