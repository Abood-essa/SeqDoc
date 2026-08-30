using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels.EntityFramework;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests;

public sealed class EntityFramework6ModelTests
{
    private static readonly CompilationProfile Profile = CompilationProfile.Create("tests/fixtures/PassC/EntityFramework6Edmx/EntityFramework6Edmx.csproj", "Release", "net9.0");
    private const string Ef = "EntityFramework";
    private const string Version = "6.0.0.0";
    private const string Context = "InitialRedTest.RecordsContext";
    private const string Entity = "InitialRedTest.Record";
    private const string DbSet = "System.Data.Entity.DbSet<InitialRedTest.Record>";

    [Theory]
    [InlineData("FirstOrDefault", EntityFrameworkQueryOperatorKind.FirstOrDefault)]
    [InlineData("Count", EntityFrameworkQueryOperatorKind.Count)]
    public async Task ExactEf6QueryOverloadsProduceOneEvidenceBackedFact(string name, EntityFrameworkQueryOperatorKind expected)
    {
        var operation = Query(name);
        var result = await new EntityFramework6Model().AnalyzeOperationAsync(operation, ContextFor(), CancellationToken.None);

        var fact = Assert.IsType<EntityFrameworkQueryFact>(Assert.Single(result.Facts));
        Assert.True(result.Recognized);
        Assert.Equal(expected, Assert.Single(fact.Chain).OperatorKind);
        Assert.Equal(Context, fact.DbContextType);
        Assert.Equal(Entity, fact.EntityType);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        Assert.NotEmpty(fact.Evidence);
        Assert.All(fact.Evidence, evidence => Assert.Equal(EvidenceKind.FrameworkModel, evidence.Kind));
    }

    [Fact]
    public async Task CountOverOneExactWhereStepIsAdmittedButForeignWhereIsNot()
    {
        var operation = Query("Count") with
        {
            QueryChain = new FrameworkQueryChainDescriptor(
                DbSet, Context, "Records", Entity,
                [new FrameworkChainStepDescriptor(new OperationId("where"), Where(), null)]),
        };
        var admitted = await new EntityFramework6Model().AnalyzeOperationAsync(operation, ContextFor(), CancellationToken.None);
        Assert.IsType<EntityFrameworkQueryFact>(Assert.Single(admitted.Facts));

        var rejected = await new EntityFramework6Model().AnalyzeOperationAsync(operation with
        {
            QueryChain = operation.QueryChain with
            {
                Steps = [new FrameworkChainStepDescriptor(new OperationId("foreign-where"), Where() with { AssemblyIdentity = "Foreign.Linq" }, null)],
            },
        }, ContextFor(), CancellationToken.None);
        Assert.False(rejected.Recognized);
        Assert.Empty(rejected.Facts);
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("SaveChanges")]
    public async Task ExactEf6MutationOverloadsProduceTheCorrectMutation(string name)
    {
        var result = await new EntityFramework6Model().AnalyzeOperationAsync(Mutation(name), ContextFor(), CancellationToken.None);
        var fact = Assert.IsType<EntityFrameworkMutationFact>(Assert.Single(result.Facts));

        Assert.True(result.Recognized);
        Assert.Equal(name, fact.MutationKind.ToString());
        Assert.Equal(Context, fact.DbContextType);
        Assert.Equal(name == "Add" ? Entity : string.Empty, fact.EntityType);
    }

    [Fact]
    public async Task WrongAssemblyAndSignatureFailClosedEvenWhenNamesMatch()
    {
        var operation = Query("FirstOrDefault") with
        {
            TargetIdentity = Query("FirstOrDefault").TargetIdentity! with
            {
                AssemblyIdentity = "System.Linq",
                Parameters = [new(ParameterRefKind.None, "System.Linq.IQueryable<InitialRedTest.Record>"), new(ParameterRefKind.In, "System.Linq.Expressions.Expression<System.Func<InitialRedTest.Record, System.Boolean>>")],
            },
        };

        var result = await new EntityFramework6Model().AnalyzeOperationAsync(operation, ContextFor(), CancellationToken.None);
        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task MalformedEdmxDescriptorsFailClosedWithoutThrowing()
    {
        var valid = EdmxOperation(
            [new CompilerProvenArgument(0, "System.String", "project"),
             new CompilerProvenArgument(1, "System.String", "Model.edmx"),
             new CompilerProvenArgument(2, "System.String", "fingerprint"),
             new CompilerProvenArgument(3, "System.Boolean", "true"),
             new CompilerProvenArgument(4, "System.Boolean", "false")]);
        var malformed = new[]
        {
            valid with { Kind = "EdmxMetadataExtra" },
            valid with { ConstantArguments = valid.ConstantArguments.Add(new CompilerProvenArgument(5, "System.String", "extra")) },
            valid with { ConstantArguments = [valid.ConstantArguments[1], valid.ConstantArguments[0], valid.ConstantArguments[2], valid.ConstantArguments[3], valid.ConstantArguments[4]] },
            valid with { ConstantArguments = [new CompilerProvenArgument(0, "System.Int32", "1"), valid.ConstantArguments[1], valid.ConstantArguments[2], valid.ConstantArguments[3], valid.ConstantArguments[4]] },
            valid with { ConstantArguments = [new CompilerProvenArgument(0, "System.String", null, isNull: true), valid.ConstantArguments[1], valid.ConstantArguments[2], valid.ConstantArguments[3], valid.ConstantArguments[4]] },
            valid with { ConstantArguments = [valid.ConstantArguments[0], valid.ConstantArguments[1], valid.ConstantArguments[2], new CompilerProvenArgument(3, "System.Boolean", "TRUE"), valid.ConstantArguments[4]] },
        };

        foreach (var operation in malformed)
        {
            var result = await new EntityFramework6Model().AnalyzeOperationAsync(operation, ContextFor(), CancellationToken.None);
            Assert.False(result.Recognized);
            Assert.Empty(result.Facts);
        }
    }

    [Fact]
    public async Task QueryAndMutationAdmissionRequiresInvocationKind()
    {
        foreach (var operation in new[]
                 {
                     Query("Count") with { Kind = "Expression" },
                     Mutation("Add") with { Kind = "Expression" },
                 })
        {
            var result = await new EntityFramework6Model().AnalyzeOperationAsync(operation, ContextFor(), CancellationToken.None);
            Assert.False(result.Recognized);
            Assert.Empty(result.Facts);
        }
    }

    [Theory]
    [InlineData("EntityFramework", "6.0.0.0", true)]
    [InlineData("EntityFramework", "6.0.1.0", false)]
    [InlineData("EntityFrameworkCore", "6.0.0.0", false)]
    public void ApplicabilityRequiresTheExactEf6Reference(string assembly, string version, bool expected)
    {
        var index = EmptyIndex([new ProgramReference("ref", new ProjectId("project"), ProgramReferenceKind.Package, assembly, version, [])]);
        Assert.Equal(expected, new EntityFramework6Model().IsApplicable(new FrameworkDetectionContext(Profile, index)));
    }

    private static FrameworkAnalysisContext ContextFor() => new(Profile, EmptyIndex());

    private static OperationDescriptor Query(string name)
    {
        ImmutableArray<ParameterIdentityDescriptor> parameters = name == "Count"
            ? ImmutableArray.Create(new ParameterIdentityDescriptor(ParameterRefKind.None, $"System.Linq.IQueryable<{Entity}>"))
            : ImmutableArray.Create(new ParameterIdentityDescriptor(ParameterRefKind.None, $"System.Linq.IQueryable<{Entity}>"), new ParameterIdentityDescriptor(ParameterRefKind.None, $"System.Linq.Expressions.Expression<System.Func<{Entity}, System.Boolean>>"));
        return new(new OperationId("op:" + name), new MethodId("method:Execute"), "Invocation", new DocumentId("doc"), 10, 1, [Evidence()], CertaintyLevel.Exact,
            new FrameworkMethodIdentity("System.Linq.Queryable", "System.Linq.Queryable", name, 1, parameters, name == "Count" ? "System.Int32" : Entity, "9.0.0.0"),
            QueryChain: new FrameworkQueryChainDescriptor(DbSet, Context, "Records", Entity, []),
            PredicateShape: name == "FirstOrDefault" ? new(PredicateShapeKind.EqualityComparison, new OperationId("predicate")) : null);
    }

    private static OperationDescriptor Mutation(string name) => new(new OperationId("op:" + name), new MethodId("method:Execute"), "Invocation", new DocumentId("doc"), name == "Add" ? 20 : 30, 1, [Evidence()], CertaintyLevel.Exact,
        new FrameworkMethodIdentity(Ef, name == "Add" ? "System.Data.Entity.DbSet`1" : "System.Data.Entity.DbContext", name, 0,
            name == "Add" ? [new(ParameterRefKind.None, Entity)] : [], name == "Add" ? Entity : "System.Int32", Version),
            QueryChain: new FrameworkQueryChainDescriptor(DbSet, Context, "Records", Entity, []));

    private static FrameworkMethodIdentity Where() => new(
        "System.Linq.Queryable", "System.Linq.Queryable", "Where", 1,
        [new(ParameterRefKind.None, $"System.Linq.IQueryable<{Entity}>"), new(ParameterRefKind.None, $"System.Linq.Expressions.Expression<System.Func<{Entity}, System.Boolean>>")],
        $"System.Linq.IQueryable<{Entity}>", "9.0.0.0");

    private static OperationDescriptor EdmxOperation(ImmutableArray<CompilerProvenArgument> arguments)
        => new(new OperationId("op:edmx"), new MethodId("method:metadata"), "EdmxMetadata", new DocumentId("doc"), 1, 1,
            [Evidence()], CertaintyLevel.Exact, ConstantArguments: arguments);

    private static EvidenceRef Evidence() => new(new EvidenceId("source"), EvidenceKind.Source, "Operations.cs", new SourceRange(new DocumentId("doc"), new SourcePosition(1, 0), new SourcePosition(1, 1)), "Execute", null, CertaintyLevel.Exact);

    private static ProgramIndexSnapshot EmptyIndex(ImmutableArray<ProgramReference>? references = null) => new(1, "test", Profile, [], [], [], [], [], [], [], references ?? [new("ref", new ProjectId("project"), ProgramReferenceKind.Package, Ef, Version, [])], [], [], [], "input", "fingerprint");
}
