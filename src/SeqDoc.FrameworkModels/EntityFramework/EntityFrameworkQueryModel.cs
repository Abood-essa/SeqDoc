using System.Collections.Immutable;
using System.Text;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.FrameworkModels.EntityFramework;

/// <summary>
/// Versioned Entity Framework Core query model. It admits only the exact SingleOrDefaultAsync and
/// FirstOrDefaultAsync terminals on EntityFrameworkQueryableExtensions whose compiler-proven
/// receiver chain contains ordered AsNoTracking and Include steps and whose predicate is an equality
/// comparison linked to a comparison semantic fact. It matches exact assembly, assembly version,
/// containing metadata type, metadata method name, and generic arity; it never matches raw names and
/// never guesses a query from unsupported terminals, lookalikes, or non-equality predicates.
/// </summary>
public sealed class EntityFrameworkQueryModel : IFrameworkBehaviorModel
{
    public const string ModelIdValue = "seqdoc.entityframework.queries";
    public const string ModelVersionValue = "1.0.0";

    /// <summary>Exact fully qualified framework identities admitted by this model version.</summary>
    internal static class Identity
    {
        public const string EfCoreAssembly = "Microsoft.EntityFrameworkCore";
        public const string QueryableExtensionsType = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
        public const string LinqQueryableAssembly = "System.Linq.Queryable";
        public const string LinqQueryableType = "System.Linq.Queryable";
    }

    public FrameworkModelDescriptor Descriptor { get; } = new(
        ModelIdValue,
        ModelVersionValue,
        "Entity Framework Core Queries",
        Order: 200);

    /// <summary>
    /// Applies when the unmodified Program Index contains an exact Microsoft.EntityFrameworkCore
    /// package or assembly reference. The fixture and admitted real application both carry the exact
    /// reference; a profile without the framework never admits an EF query fact.
    /// </summary>
    public bool IsApplicable(FrameworkDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ProgramIndex.References.Any(reference =>
            string.Equals(reference.Identity, Identity.EfCoreAssembly, StringComparison.Ordinal));
    }

    public ValueTask<ModelResult> AnalyzeSymbolAsync(
        SymbolDescriptor symbol,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ModelResult.Unrecognized);
    }

    public ValueTask<ModelResult> AnalyzeOperationAsync(
        OperationDescriptor operation,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(AnalyzeQuery(operation, context));
    }

    private ModelResult AnalyzeQuery(OperationDescriptor operation, FrameworkAnalysisContext context)
    {
        if (!string.Equals(operation.Kind, "Invocation", StringComparison.Ordinal)
            || operation.TargetIdentity is null
            || operation.QueryChain is null)
        {
            return ModelResult.Unrecognized;
        }

        var identity = operation.TargetIdentity;
        var terminal = ResolveTerminalKind(identity);
        if (terminal is null
            || !string.Equals(identity.AssemblyIdentity, Identity.EfCoreAssembly, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(identity.AssemblyVersion)
            || !string.Equals(identity.ContainingMetadataType, Identity.QueryableExtensionsType, StringComparison.Ordinal)
            || identity.GenericArity != 1)
        {
            // A different assembly, an unproven assembly version, or a lookalike containing type
            // never produces an exact query; nothing is guessed.
            return ModelResult.Unrecognized;
        }

        if (terminal.Value == EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync
            && !HasExactFirstOrDefaultAsyncSignature(identity, operation.QueryChain))
        {
            // The FirstOrDefaultAsync terminal is admitted only for the exact supported EF Core
            // declaration. Any other parameter shape, ref-kind, return type, malformed type string,
            // or generic element that differs from the proven receiver chain element fails closed;
            // an alternate overload is never guessed as the exact query.
            return ModelResult.Unrecognized;
        }

        var diagnostics = new List<AnalysisDiagnostic>();
        var chainValidation = ValidateChain(operation.QueryChain);
        if (chainValidation is not null)
        {
            diagnostics.Add(EntityFrameworkQueryModelDiagnostics.UnsupportedQueryChain(
                context.Profile.Id,
                BuildSubject(operation, chainValidation)));
            return new ModelResult(false, diagnostics: diagnostics.ToImmutableArray());
        }

        var requiresEqualityPredicate = terminal.Value is EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync
            or EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync;
        if (requiresEqualityPredicate
            && (operation.PredicateShape is not { Kind: PredicateShapeKind.EqualityComparison }
                || operation.PredicateShape?.ComparisonOperation is null))
        {
            diagnostics.Add(EntityFrameworkQueryModelDiagnostics.NonEqualityPredicate(
                context.Profile.Id,
                BuildSubject(operation, operation.PredicateShape?.Kind.ToString() ?? PredicateShapeKind.None.ToString())));
            return new ModelResult(false, diagnostics: diagnostics.ToImmutableArray());
        }

        var inputCertainty = operation.Certainty;
        var effectiveCertainty = inputCertainty == CertaintyLevel.Exact ? CertaintyLevel.Exact : inputCertainty;
        if (inputCertainty != CertaintyLevel.Exact)
        {
            diagnostics.Add(EntityFrameworkQueryModelDiagnostics.DegradedInputCertainty(
                context.Profile.Id,
                operation.Id.Value));
        }

        var chain = BuildChainItems(operation, operation.QueryChain);
        var fact = new EntityFrameworkQueryFact
        {
            Id = StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
                context.Profile.Id,
                Descriptor.ModelId,
                Descriptor.Version,
                "ef-query",
                new OperationBehaviorFactAnchor(operation.Method, operation.Id),
                0)),
            Method = operation.Method,
            Operation = operation.Id,
            DbContextType = operation.QueryChain.ContainingType,
            DbSetMemberType = operation.QueryChain.ReceiverType,
            EntityType = operation.QueryChain.EntityType,
            Chain = chain,
            PredicateOperation = terminal.Value == EntityFrameworkQueryOperatorKind.CountAsync
                ? null
                : operation.PredicateShape?.ComparisonOperation,
            PredicateOperator = ComparisonOperatorKind.Equal,
            Evidence = CreateModelEvidence(
                $"query:{operation.Id.Value}:{operation.QueryChain.EntityType}",
                operation.Evidence,
                effectiveCertainty),
            Certainty = effectiveCertainty,
        };
        return new ModelResult(true, facts: [fact], diagnostics: diagnostics.ToImmutableArray());
    }

    private static EntityFrameworkQueryOperatorKind? ResolveTerminalKind(FrameworkMethodIdentity identity)
        => identity.MethodMetadataName switch
        {
            "SingleOrDefaultAsync" => EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync,
            "FirstOrDefaultAsync" => EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync,
            "CountAsync" => EntityFrameworkQueryOperatorKind.CountAsync,
            _ => null,
        };

    /// <summary>
    /// Requires the exact admitted EF Core declaration
    /// FirstOrDefaultAsync&lt;T&gt;(IQueryable&lt;T&gt;, Expression&lt;Func&lt;T, System.Boolean&gt;&gt;,
    /// CancellationToken) returning Task&lt;T&gt;. The predicate display is the compiler's exact
    /// fully qualified <c>System.Boolean</c> form, never the <c>bool</c> alias; any other display,
    /// ref-kind, return type, or generic element fails closed. All three parameters must be plain
    /// (no ref/out/in), and the generic element must be single, nonblank, and exact-equal across
    /// the receiver, predicate, return type, and the proven receiver chain entity type; malformed
    /// or nested generic declarations fail closed instead of being guessed.
    /// </summary>
    private static bool HasExactFirstOrDefaultAsyncSignature(
        FrameworkMethodIdentity identity,
        FrameworkQueryChainDescriptor? chain)
    {
        var parameters = identity.Parameters;
        if (parameters.IsDefaultOrEmpty
            || parameters.Length != 3
            || parameters[0].RefKind != ParameterRefKind.None
            || parameters[1].RefKind != ParameterRefKind.None
            || parameters[2].RefKind != ParameterRefKind.None)
        {
            return false;
        }

        if (!TryExtractSingleTypeArgument(parameters[0].FullyQualifiedType, "System.Linq.IQueryable", out var entityType))
        {
            return false;
        }

        return string.Equals(
                parameters[1].FullyQualifiedType,
                $"System.Linq.Expressions.Expression<System.Func<{entityType}, System.Boolean>>",
                StringComparison.Ordinal)
            && string.Equals(
                parameters[2].FullyQualifiedType,
                "System.Threading.CancellationToken",
                StringComparison.Ordinal)
            && string.Equals(
                identity.ReturnType,
                $"System.Threading.Tasks.Task<{entityType}>",
                StringComparison.Ordinal)
            && chain is not null
            && string.Equals(entityType, chain.EntityType, StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts the single non-nested type argument of a fully qualified open generic declaration
    /// such as <c>System.Linq.IQueryable&lt;T&gt;</c>. Blank, malformed, open, or nested generic
    /// declarations fail so an alternate signature never supplies a guessed element type.
    /// </summary>
    private static bool TryExtractSingleTypeArgument(
        string typeName,
        string openGenericTypeName,
        out string? typeArgument)
    {
        typeArgument = null;
        var prefix = openGenericTypeName + "<";
        if (string.IsNullOrWhiteSpace(typeName)
            || !typeName.StartsWith(prefix, StringComparison.Ordinal)
            || !typeName.EndsWith('>'))
        {
            return false;
        }

        var argument = typeName.Substring(prefix.Length, typeName.Length - prefix.Length - 1);
        if (string.IsNullOrWhiteSpace(argument)
            || argument.Contains('<')
            || argument.Contains('>'))
        {
            return false;
        }

        typeArgument = argument;
        return true;
    }

    /// <summary>
    /// Validates the compiler-proven receiver chain. The base receiver must name a DbContext, a DbSet
    /// member, and an entity type; every step must be an exact AsNoTracking (at most once), an exact
    /// Include or SelectMany carrying a navigation-member identity, or an exact Where carrying a
    /// predicate; any other operator fails closed.
    /// </summary>
    private static string? ValidateChain(FrameworkQueryChainDescriptor chain)
    {
        if (string.IsNullOrWhiteSpace(chain.ContainingType)
            || string.IsNullOrWhiteSpace(chain.MemberName)
            || string.IsNullOrWhiteSpace(chain.ReceiverType)
            || string.IsNullOrWhiteSpace(chain.EntityType))
        {
            return "blank-base-receiver";
        }

        if (chain.Steps.IsDefault)
        {
            return "uninitialized-chain";
        }

        var noTrackingSeen = false;
        foreach (var step in chain.Steps)
        {
            if (step.TargetIdentity is null)
            {
                return "blank-step-identity";
            }

            if (IsExactStep(step.TargetIdentity, "AsNoTracking", 1))
            {
                if (noTrackingSeen)
                {
                    return "duplicate-as-no-tracking";
                }

                noTrackingSeen = true;
                continue;
            }

            if (IsExactStep(step.TargetIdentity, "Include", 2))
            {
                if (string.IsNullOrWhiteSpace(step.NavigationMemberIdentity))
                {
                    return "include-without-navigation";
                }

                continue;
            }

            if (IsExactLinqStep(step.TargetIdentity, "Where", 1))
            {
                // A Where step is exact by its framework identity alone; compound predicates with
                // several member accesses and predicates over framework time values do not carry a
                // single navigation anchor, and that must never withhold the exact filter step.
                continue;
            }

            if (IsExactLinqStep(step.TargetIdentity, "SelectMany", 2))
            {
                if (string.IsNullOrWhiteSpace(step.NavigationMemberIdentity))
                {
                    return "select-many-without-navigation";
                }

                continue;
            }

            return $"unsupported-step:{step.TargetIdentity.MethodMetadataName}";
        }

        return null;
    }

    private static bool IsExactStep(FrameworkMethodIdentity step, string methodMetadataName, int arity)
        => string.Equals(step.AssemblyIdentity, Identity.EfCoreAssembly, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(step.AssemblyVersion)
            && string.Equals(step.ContainingMetadataType, Identity.QueryableExtensionsType, StringComparison.Ordinal)
            && string.Equals(step.MethodMetadataName, methodMetadataName, StringComparison.Ordinal)
            && step.GenericArity == arity;

    private static bool IsExactLinqStep(FrameworkMethodIdentity step, string methodMetadataName, int arity)
        => string.Equals(step.AssemblyIdentity, Identity.LinqQueryableAssembly, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(step.AssemblyVersion)
            && string.Equals(step.ContainingMetadataType, Identity.LinqQueryableType, StringComparison.Ordinal)
            && string.Equals(step.MethodMetadataName, methodMetadataName, StringComparison.Ordinal)
            && step.GenericArity == arity;

    private static ImmutableArray<EntityFrameworkQueryChainItem> BuildChainItems(
        OperationDescriptor operation,
        FrameworkQueryChainDescriptor chain)
    {
        var builder = ImmutableArray.CreateBuilder<EntityFrameworkQueryChainItem>();
        foreach (var step in chain.Steps)
        {
            if (IsExactStep(step.TargetIdentity!, "AsNoTracking", 1))
            {
                builder.Add(new EntityFrameworkQueryChainItem(
                    EntityFrameworkQueryOperatorKind.AsNoTracking,
                    step.Operation,
                    null));
            }
            else if (IsExactStep(step.TargetIdentity!, "Include", 2))
            {
                builder.Add(new EntityFrameworkQueryChainItem(
                    EntityFrameworkQueryOperatorKind.Include,
                    step.Operation,
                    step.NavigationMemberIdentity));
            }
            else if (IsExactLinqStep(step.TargetIdentity!, "Where", 1))
            {
                builder.Add(new EntityFrameworkQueryChainItem(
                    EntityFrameworkQueryOperatorKind.Where,
                    step.Operation,
                    null));
            }
            else if (IsExactLinqStep(step.TargetIdentity!, "SelectMany", 2))
            {
                builder.Add(new EntityFrameworkQueryChainItem(
                    EntityFrameworkQueryOperatorKind.SelectMany,
                    step.Operation,
                    step.NavigationMemberIdentity));
            }
        }

        builder.Add(new EntityFrameworkQueryChainItem(
            ResolveTerminalKind(operation.TargetIdentity!)!.Value,
            operation.Id,
            null));
        return builder.ToImmutable();
    }

    private static string BuildSubject(OperationDescriptor operation, string detail)
        => string.Join('\u001f', operation.Id.Value, operation.Method.Value, detail);

    /// <summary>
    /// Builds the single framework-model evidence record for one query fact. The evidence identity
    /// hashes the producing descriptor, a stable query subject, the effective certainty, and the
    /// complete canonical underlying evidence-ID sequence, so records with different payloads never
    /// share one identity while semantically identical evidence remains deterministic.
    /// </summary>
    private ImmutableArray<EvidenceRef> CreateModelEvidence(
        string subject,
        ImmutableArray<EvidenceRef> underlying,
        CertaintyLevel certainty)
    {
        var canonical = underlying
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var artifact = $"{Descriptor.ModelId}:{Descriptor.Version}";
        var evidencePayload = $"{subject}\u001f{string.Join('\u001f', canonical.Select(item => item.Id.Value))}";
        var id = StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(
            EvidenceKind.FrameworkModel,
            artifact,
            null,
            null,
            null,
            null,
            certainty,
            Descriptor.ModelId,
            Descriptor.Version,
            Detail: evidencePayload));
        return
        [
            new EvidenceRef(
                id,
                EvidenceKind.FrameworkModel,
                artifact,
                range: null,
                symbol: null,
                detail: evidencePayload,
                certainty,
                canonical,
                Descriptor.ModelId,
                Descriptor.Version),
        ];
    }
}
