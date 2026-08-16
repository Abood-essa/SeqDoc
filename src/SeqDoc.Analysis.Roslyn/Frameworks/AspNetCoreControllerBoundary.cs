using Microsoft.CodeAnalysis;

namespace SeqDoc.Analysis.Roslyn.Frameworks;

/// <summary>
/// Compiler-proven ASP.NET Core controller boundary used by the companion DI projection. It mirrors
/// the accepted C-1 boundary (exact ApiController attribute, exact ControllerBase derivation,
/// NonController honored, public non-abstract non-static class) but resolves the authoritative
/// attribute and base symbols from each loaded compilation and compares original definitions with
/// <see cref="SymbolEqualityComparer"/>, so lookalike attributes and base types never admit a type.
/// </summary>
internal static class AspNetCoreControllerBoundary
{
    private const string ApiControllerAttributeMetadataName = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
    private const string NonControllerAttributeMetadataName = "Microsoft.AspNetCore.Mvc.NonControllerAttribute";
    private const string ControllerBaseMetadataName = "Microsoft.AspNetCore.Mvc.ControllerBase";

    /// <summary>
    /// Returns true only when the type is an exact admitted ASP.NET controller: a public, non-static,
    /// non-abstract, non-generic class that carries the exact <c>ApiControllerAttribute</c>, does not
    /// carry the exact <c>NonControllerAttribute</c>, and derives from the exact
    /// <c>ControllerBase</c> resolved from the same compilation.
    /// </summary>
    public static bool IsExactAdmittedController(INamedTypeSymbol type, Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(compilation);

        if (type.TypeKind != TypeKind.Class
            || !IsPublicOrNestedPublic(type)
            || type.IsAbstract
            || type.IsStatic
            || type.Arity != 0)
        {
            return false;
        }

        var apiController = compilation.GetTypeByMetadataName(ApiControllerAttributeMetadataName);
        var nonController = compilation.GetTypeByMetadataName(NonControllerAttributeMetadataName);
        var controllerBase = compilation.GetTypeByMetadataName(ControllerBaseMetadataName);
        if (apiController is null || controllerBase is null)
        {
            return false;
        }

        var attributes = type.GetAttributes();
        if (!attributes.Any(attribute =>
                attribute.AttributeClass is not null
                && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass.OriginalDefinition, apiController)))
        {
            return false;
        }

        if (nonController is not null
            && attributes.Any(attribute =>
                attribute.AttributeClass is not null
                && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass.OriginalDefinition, nonController)))
        {
            // NonController is honored exactly: an otherwise ControllerBase-derived type carrying the
            // exact attribute is deliberately not a controller and never produces DI bindings.
            return false;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, controllerBase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPublicOrNestedPublic(INamedTypeSymbol type)
    {
        if (type.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        return type.ContainingType is null || IsPublicOrNestedPublic(type.ContainingType);
    }
}
