using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Core.Tests.Persistence;

public sealed class PersistenceContractsTests
{
    private static EvidenceRef CreateEvidence(CertaintyLevel certainty = CertaintyLevel.Exact)
        => new(
            new EvidenceId("evidence:v1:persistence"),
            EvidenceKind.Source,
            "src/Data/AppDbContext.cs",
            range: null,
            symbol: "AppDbContext.Save",
            detail: null,
            certainty);

    [Fact]
    public void EntityFrameworkQueryChainItemEnforcesOperatorShapeAndNavigationRules()
    {
        var opId = new OperationId("operation:1");
        var predId = new OperationId("pred:1");

        // Valid operators
        var asNoTracking = new EntityFrameworkQueryChainItem(EntityFrameworkQueryOperatorKind.AsNoTracking, opId, null);
        Assert.Equal(EntityFrameworkQueryOperatorKind.AsNoTracking, asNoTracking.OperatorKind);
        Assert.Equal(opId, asNoTracking.Operation);
        Assert.Null(asNoTracking.NavigationMember);
        Assert.Null(asNoTracking.PredicateOperation);

        var include = new EntityFrameworkQueryChainItem(EntityFrameworkQueryOperatorKind.Include, opId, "Order.Items");
        Assert.Equal(EntityFrameworkQueryOperatorKind.Include, include.OperatorKind);
        Assert.Equal("Order.Items", include.NavigationMember);

        var where = new EntityFrameworkQueryChainItem(
            EntityFrameworkQueryOperatorKind.Where,
            opId,
            null,
            predicateOperation: predId,
            predicateOperator: ComparisonOperatorKind.Equal);
        Assert.Equal(EntityFrameworkQueryOperatorKind.Where, where.OperatorKind);
        Assert.Equal(predId, where.PredicateOperation);
        Assert.Equal(ComparisonOperatorKind.Equal, where.PredicateOperator);

        // Invalid inputs
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityFrameworkQueryChainItem((EntityFrameworkQueryOperatorKind)999, opId, null));
        Assert.Throws<ArgumentException>(() => new EntityFrameworkQueryChainItem(EntityFrameworkQueryOperatorKind.AsNoTracking, new OperationId(" "), null));
        Assert.Throws<ArgumentException>(() => new EntityFrameworkQueryChainItem(EntityFrameworkQueryOperatorKind.Include, opId, " "));
        Assert.Throws<ArgumentException>(() => new EntityFrameworkQueryChainItem(EntityFrameworkQueryOperatorKind.SelectMany, opId, ""));
        Assert.Throws<ArgumentException>(() => new EntityFrameworkQueryChainItem(
            EntityFrameworkQueryOperatorKind.Include,
            opId,
            "Order.Items",
            predicateOperation: predId));
    }

    [Fact]
    public void EntityFrameworkQueryFactRetainsDeclaredFieldsAndEvidence()
    {
        var methodId = new MethodId("method:v1:GetOrder");
        var opId = new OperationId("operation:1");
        var predId = new OperationId("pred:1");
        var evidence = ImmutableArray.Create(CreateEvidence());

        var chain = ImmutableArray.Create(
            new EntityFrameworkQueryChainItem(EntityFrameworkQueryOperatorKind.AsNoTracking, opId, null),
            new EntityFrameworkQueryChainItem(EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync, opId, null));

        var fact = new EntityFrameworkQueryFact
        {
            Id = new BehaviorFactId("ef-query:1"),
            Method = methodId,
            Operation = opId,
            DbContextType = "Acme.Data.AppDbContext",
            DbSetMemberType = "Microsoft.EntityFrameworkCore.DbSet<Acme.Models.Order>",
            EntityType = "Acme.Models.Order",
            Chain = chain,
            PredicateOperation = predId,
            PredicateOperator = ComparisonOperatorKind.Equal,
            Evidence = evidence,
            Certainty = CertaintyLevel.Exact,
        };

        Assert.Equal(methodId, fact.Method);
        Assert.Equal(opId, fact.Operation);
        Assert.Equal("Acme.Data.AppDbContext", fact.DbContextType);
        Assert.Equal("Microsoft.EntityFrameworkCore.DbSet<Acme.Models.Order>", fact.DbSetMemberType);
        Assert.Equal("Acme.Models.Order", fact.EntityType);
        Assert.Equal(chain, fact.Chain);
        Assert.Equal(predId, fact.PredicateOperation);
        Assert.Equal(ComparisonOperatorKind.Equal, fact.PredicateOperator);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        Assert.Single(fact.Evidence);
    }

    [Fact]
    public void EntityFrameworkMutationFactRetainsMutationKindAndContext()
    {
        var methodId = new MethodId("method:v1:CreateOrder");
        var opId = new OperationId("operation:1");
        var argId = new OperationId("arg:1");
        var evidence = ImmutableArray.Create(CreateEvidence());

        var mutation = new EntityFrameworkMutationFact
        {
            Id = new BehaviorFactId("ef-mutation:1"),
            Method = methodId,
            Operation = opId,
            MutationKind = EntityFrameworkMutationKind.Add,
            SequenceOrdinal = 0,
            DbContextType = "Acme.Data.AppDbContext",
            EntityType = "Acme.Models.Order",
            ArgumentOperation = argId,
            TargetMember = null,
            Evidence = evidence,
            Certainty = CertaintyLevel.Exact,
        };

        Assert.Equal(methodId, mutation.Method);
        Assert.Equal(opId, mutation.Operation);
        Assert.Equal(EntityFrameworkMutationKind.Add, mutation.MutationKind);
        Assert.Equal(0, mutation.SequenceOrdinal);
        Assert.Equal("Acme.Data.AppDbContext", mutation.DbContextType);
        Assert.Equal("Acme.Models.Order", mutation.EntityType);
        Assert.Equal(argId, mutation.ArgumentOperation);
        Assert.Null(mutation.TargetMember);
    }

    [Fact]
    public void StateAssignmentSemanticFactEnforcesShapeAndEvidence()
    {
        var factId = new SemanticFactId("state-assignment:1");
        var methodId = new MethodId("method:v1:UpdateStatus");
        var opId = new OperationId("operation:1");
        var evidence = ImmutableArray.Create(CreateEvidence());

        var assignment = new StateAssignmentSemanticFact(
            factId,
            methodId,
            opId,
            "Acme.Models.Order.Status",
            "Acme.Models.OrderStatus",
            StateAssignmentValueKind.EnumConstant,
            "Approved",
            evidence,
            CertaintyLevel.Exact,
            sequenceOrdinal: 2);

        Assert.Equal(factId, assignment.Id);
        Assert.Equal(methodId, assignment.Method);
        Assert.Equal(opId, assignment.Operation);
        Assert.Equal("Acme.Models.Order.Status", assignment.TargetMember);
        Assert.Equal("Acme.Models.OrderStatus", assignment.TargetType);
        Assert.Equal(StateAssignmentValueKind.EnumConstant, assignment.ValueKind);
        Assert.Equal("Approved", assignment.Value);
        Assert.Equal(2, assignment.SequenceOrdinal);

        // Validation checks
        Assert.Throws<ArgumentException>(() => new StateAssignmentSemanticFact(
            new SemanticFactId(" "), methodId, opId, "Target", "Type", StateAssignmentValueKind.Literal, "val", evidence, CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new StateAssignmentSemanticFact(
            factId, methodId, opId, " ", "Type", StateAssignmentValueKind.Literal, "val", evidence, CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new StateAssignmentSemanticFact(
            factId, methodId, opId, "Target", "Type", StateAssignmentValueKind.Literal, " ", evidence, CertaintyLevel.Exact));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StateAssignmentSemanticFact(
            factId, methodId, opId, "Target", "Type", StateAssignmentValueKind.Unknown, "val", evidence, CertaintyLevel.Exact));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StateAssignmentSemanticFact(
            factId, methodId, opId, "Target", "Type", StateAssignmentValueKind.Literal, "val", evidence, CertaintyLevel.Exact, sequenceOrdinal: -1));
        Assert.Throws<ArgumentException>(() => new StateAssignmentSemanticFact(
            factId, methodId, opId, "Target", "Type", StateAssignmentValueKind.Literal, "val", [], CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new StateAssignmentSemanticFact(
            factId, methodId, opId, "Target", "Type", StateAssignmentValueKind.Literal, "val", evidence, CertaintyLevel.Unknown));
    }

    [Fact]
    public void EfOperationSequenceFactValidatesFieldsAndOrdinal()
    {
        var methodId = new MethodId("method:v1:Execute");
        var opId = new OperationId("operation:1");

        var seq = new EfOperationSequenceFact(methodId, opId, EfOperationSequenceKind.QueryTerminal, 3);
        Assert.Equal(methodId, seq.Method);
        Assert.Equal(opId, seq.Operation);
        Assert.Equal(EfOperationSequenceKind.QueryTerminal, seq.Kind);
        Assert.Equal(3, seq.Ordinal);

        Assert.Throws<ArgumentException>(() => new EfOperationSequenceFact(new MethodId(" "), opId, EfOperationSequenceKind.Mutation, 0));
        Assert.Throws<ArgumentException>(() => new EfOperationSequenceFact(methodId, new OperationId(" "), EfOperationSequenceKind.Mutation, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EfOperationSequenceFact(methodId, opId, EfOperationSequenceKind.Unknown, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EfOperationSequenceFact(methodId, opId, EfOperationSequenceKind.Mutation, -1));
    }
}
