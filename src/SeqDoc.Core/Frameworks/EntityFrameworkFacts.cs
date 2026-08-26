using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Core.Frameworks;

/// <summary>
/// Closed vocabulary of Entity Framework query operators admitted by the translation-alpha EF query
/// model. Only compiler-proven exact-symbol operators project into this vocabulary; any other query
/// shape produces no invented fact. The terminal vocabulary distinguishes the exact single-value
/// lookups (<see cref="SingleOrDefaultAsync"/>, <see cref="FirstOrDefaultAsync"/>) from the exact
/// aggregation (<see cref="CountAsync"/>).
/// </summary>
public enum EntityFrameworkQueryOperatorKind
{
    Unknown,
    AsNoTracking,
    Include,
    Where,
    SelectMany,
    SingleOrDefaultAsync,
    FirstOrDefaultAsync,
    CountAsync,
}

/// <summary>
/// One ordered operator of an admitted EF query chain. The operation is the exact revision-local
/// invocation operation that grounds the operator, and the navigation member is the canonical
/// compiler identity of the navigation selected by an Include or SelectMany step. Only the terminal
/// <see cref="EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync"/> or
/// <see cref="EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync"/> step carries a fact-level
/// predicate anchor; Where steps carry their own predicate anchor on the chain item.
/// </summary>
public sealed record EntityFrameworkQueryChainItem
{
    public EntityFrameworkQueryChainItem(
        EntityFrameworkQueryOperatorKind operatorKind,
        OperationId operation,
        string? navigationMember,
        OperationId? predicateOperation = null,
        ComparisonOperatorKind predicateOperator = ComparisonOperatorKind.Equal)
    {
        if (!Enum.IsDefined(operatorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(operatorKind), "Undefined EF query operator kind.");
        }

        if (string.IsNullOrWhiteSpace(operation.Value))
        {
            throw new ArgumentException("An EF query chain item requires a non-empty operation anchor.", nameof(operation));
        }

        if (operatorKind is EntityFrameworkQueryOperatorKind.Include or EntityFrameworkQueryOperatorKind.SelectMany
            && string.IsNullOrWhiteSpace(navigationMember))
        {
            throw new ArgumentException("An Include or SelectMany chain item requires a navigation-member identity.", nameof(navigationMember));
        }

        if (operatorKind != EntityFrameworkQueryOperatorKind.Where && predicateOperation is not null)
        {
            throw new ArgumentException("Only a Where chain item can carry a predicate-comparison anchor.", nameof(predicateOperation));
        }

        OperatorKind = operatorKind;
        Operation = operation;
        NavigationMember = navigationMember;
        PredicateOperation = predicateOperation;
        PredicateOperator = predicateOperator;
    }

    public EntityFrameworkQueryOperatorKind OperatorKind { get; }

    public OperationId Operation { get; }

    public string? NavigationMember { get; }

    /// <summary>Exact comparison operation inside a Where predicate; null for every other step.</summary>
    public OperationId? PredicateOperation { get; }

    public ComparisonOperatorKind PredicateOperator { get; }
}

/// <summary>
/// One exact, evidence-backed Entity Framework query admitted by the translation-alpha EF model. The
/// fact records the exact DbContext, DbSet, entity type, ordered AsNoTracking/Include chain, the
/// SingleOrDefaultAsync or FirstOrDefaultAsync terminal, and the equality predicate comparison
/// anchor linked to accepted contract comparison semantic facts. Unsupported terminals, lookalike symbols,
/// non-equality predicates, and unsupported chain operators never produce a fact.
/// </summary>
public sealed record EntityFrameworkQueryFact : BehaviorFact
{
    public required MethodId Method { get; init; }

    public required OperationId Operation { get; init; }

    public required string DbContextType { get; init; }

    public required string DbSetMemberType { get; init; }

    public required string EntityType { get; init; }

    public required ImmutableArray<EntityFrameworkQueryChainItem> Chain { get; init; }

    /// <summary>
    /// The exact comparison operation inside the SingleOrDefaultAsync or FirstOrDefaultAsync
    /// predicate. Downstream joins resolve the linked <see cref="ComparisonSemanticFact"/> by
    /// (method, operation).
    /// </summary>
    public required OperationId? PredicateOperation { get; init; }

    public required ComparisonOperatorKind PredicateOperator { get; init; }
}

/// <summary>
/// Closed vocabulary of exact Entity Framework mutation operators admitted by the translation-alpha
/// EF mutation projection. Only compiler-proven exact-symbol operators project into this vocabulary;
/// lookalikes and unsupported mutation shapes produce no invented fact.
/// </summary>
public enum EntityFrameworkMutationKind
{
    Unknown,
    Add,
    RemoveRange,
    Clear,
    SaveChangesAsync,
    SaveChanges,
}

/// <summary>
/// One exact, evidence-backed Entity Framework mutation admitted by the translation-alpha EF model.
/// The fact records the exact method, the invocation operation, the mutation kind, a deterministic
/// source-order ordinal within the method, and the DbContext/entity identities the compiler proved.
/// Add/RemoveRange carry the added/removed entity argument operation; Clear names the tracked set;
/// SaveChangesAsync records the DbContext. Unsupported terminals, lookalike symbols, and unproven
/// receivers never produce a fact.
/// </summary>
public sealed record EntityFrameworkMutationFact : BehaviorFact
{
    public required MethodId Method { get; init; }

    public required OperationId Operation { get; init; }

    public required EntityFrameworkMutationKind MutationKind { get; init; }

    /// <summary>Deterministic source-order position of the mutation within its method.</summary>
    public required int SequenceOrdinal { get; init; }

    /// <summary>Canonical DbContext identity; empty when the compiler could not prove one.</summary>
    public required string DbContextType { get; init; }

    /// <summary>Canonical entity identity; empty when the mutation is not entity-typed.</summary>
    public required string EntityType { get; init; }

    /// <summary>Exact argument operation for Add/RemoveRange mutations.</summary>
    public OperationId? ArgumentOperation { get; init; }

    /// <summary>Canonical tracked-set member for Clear mutations.</summary>
    public string? TargetMember { get; init; }
}
