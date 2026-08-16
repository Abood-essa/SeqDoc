using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels.EntityFramework;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.EntityFramework;

public sealed class EntityFrameworkQueryModelTests
{
    private const string EfCoreAssembly = "Microsoft.EntityFrameworkCore";
    private const string QueryableExtensionsType = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string EfCoreAssemblyVersion = "10.0.10.0";
    private const string DbContextType = "GetMeaning.Data.GadgetDbContext";
    private const string DbSetType = "Microsoft.EntityFrameworkCore.DbSet<GetMeaning.Models.Gadget>";
    private const string EntityType = "GetMeaning.Models.Gadget";
    private static readonly CompilationProfile Profile = CompilationProfile.Create(
        "tests/fixtures/BehaviorDocumentation/GetMeaning/GetMeaning.csproj",
        "Release",
        "net10.0");

    [Theory]
    [InlineData("SingleOrDefaultAsync", EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync)]
    [InlineData("FirstOrDefaultAsync", EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync)]
    public async Task ExactTerminalAndChainProduceQueryFact(
        string terminalName,
        EntityFrameworkQueryOperatorKind expectedTerminal)
    {
        var model = new EntityFrameworkQueryModel();
        var operation = CreateQueryOperation(terminalName, chain: AdmittedChain(), predicate: EqualityPredicate());
        var result = await model.AnalyzeOperationAsync(
            operation,
            new FrameworkAnalysisContext(Profile, EmptyIndex()),
            CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.IsType<EntityFrameworkQueryFact>(Assert.Single(result.Facts));
        Assert.Equal(operation.Method, fact.Method);
        Assert.Equal(operation.Id, fact.Operation);
        Assert.Equal(DbContextType, fact.DbContextType);
        Assert.Equal(DbSetType, fact.DbSetMemberType);
        Assert.Equal(EntityType, fact.EntityType);
        Assert.Equal(ComparisonOperatorKind.Equal, fact.PredicateOperator);
        Assert.Equal(PredicateOperationId, fact.PredicateOperation);
        Assert.Equal(
            new[]
            {
                EntityFrameworkQueryOperatorKind.AsNoTracking,
                EntityFrameworkQueryOperatorKind.Include,
                EntityFrameworkQueryOperatorKind.Include,
                expectedTerminal,
            },
            fact.Chain.Select(item => item.OperatorKind));
        Assert.Equal("GetMeaning.Models.Gadget.Parts",
            fact.Chain.Single(item => item.NavigationMember == "GetMeaning.Models.Gadget.Parts").NavigationMember);
        Assert.Equal("GetMeaning.Models.Gadget.Category",
            fact.Chain.Single(item => item.NavigationMember == "GetMeaning.Models.Gadget.Category").NavigationMember);
    }

    [Fact]
    public async Task UnsupportedTerminalsLookalikesAndInvalidChainsFailClosed()
    {
        var model = new EntityFrameworkQueryModel();
        var context = new FrameworkAnalysisContext(Profile, EmptyIndex());

        // Unsupported terminal with the same queryable containing type but a different metadata name.
        var unsupported = await model.AnalyzeOperationAsync(
            CreateQueryOperation("LastOrDefaultAsync", chain: AdmittedChain(), predicate: EqualityPredicate()),
            context,
            CancellationToken.None);
        Assert.False(unsupported.Recognized);
        Assert.Empty(unsupported.Facts);

        // Lookalike assembly: the same terminal name in the fixture assembly never matches.
        var lookalike = await model.AnalyzeOperationAsync(
            CreateQueryOperation(
                "SingleOrDefaultAsync",
                assembly: "GetMeaning",
                chain: AdmittedChain(),
                predicate: EqualityPredicate()),
            context,
            CancellationToken.None);
        Assert.False(lookalike.Recognized);
        Assert.Empty(lookalike.Facts);

        // Duplicate AsNoTracking and an unsupported chain operator fail closed with diagnostics.
        var duplicateChain = new FrameworkQueryChainDescriptor(
            DbSetType,
            DbContextType,
            "Gadgets",
            EntityType,
            [
                ChainStep("AsNoTracking", 1, null),
                ChainStep("AsNoTracking", 1, null),
            ]);
        var duplicate = await model.AnalyzeOperationAsync(
            CreateQueryOperation("SingleOrDefaultAsync", chain: duplicateChain, predicate: EqualityPredicate()),
            context,
            CancellationToken.None);
        Assert.False(duplicate.Recognized);
        Assert.Empty(duplicate.Facts);
        Assert.Contains(duplicate.Diagnostics, diagnostic => diagnostic.Code == EntityFrameworkQueryModelDiagnostics.UnsupportedQueryChainCode);

        var whereChain = new FrameworkQueryChainDescriptor(
            DbSetType,
            DbContextType,
            "Gadgets",
            EntityType,
            [ChainStep("Where", 2, null)]);
        var where = await model.AnalyzeOperationAsync(
            CreateQueryOperation("SingleOrDefaultAsync", chain: whereChain, predicate: EqualityPredicate()),
            context,
            CancellationToken.None);
        Assert.False(where.Recognized);
        Assert.Empty(where.Facts);
    }

    [Fact]
    public async Task NonEqualityPredicateFailsClosedWithDiagnostic()
    {
        var model = new EntityFrameworkQueryModel();
        var context = new FrameworkAnalysisContext(Profile, EmptyIndex());
        var result = await model.AnalyzeOperationAsync(
            CreateQueryOperation(
                "SingleOrDefaultAsync",
                chain: AdmittedChain(),
                predicate: new PredicateShapeDescriptor(PredicateShapeKind.NotEqualityComparison, PredicateOperationId)),
            context,
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == EntityFrameworkQueryModelDiagnostics.NonEqualityPredicateCode);
    }

    [Theory]
    [InlineData("GetMeaning", PredicateShapeKind.EqualityComparison)]
    [InlineData(EfCoreAssembly, PredicateShapeKind.NotEqualityComparison)]
    public async Task FirstOrDefaultAsyncWrongAssemblyOrPredicateFailsClosed(
        string assembly,
        PredicateShapeKind predicateKind)
    {
        var model = new EntityFrameworkQueryModel();
        var predicate = predicateKind == PredicateShapeKind.EqualityComparison
            ? EqualityPredicate()
            : new PredicateShapeDescriptor(predicateKind, PredicateOperationId);
        var result = await model.AnalyzeOperationAsync(
            CreateQueryOperation(
                "FirstOrDefaultAsync",
                assembly: assembly,
                chain: AdmittedChain(),
                predicate: predicate),
            new FrameworkAnalysisContext(Profile, EmptyIndex()),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Theory]
    [InlineData(FirstOrDefaultSignatureMutation.ReceiverTMismatch)]
    [InlineData(FirstOrDefaultSignatureMutation.PredicateTMismatch)]
    [InlineData(FirstOrDefaultSignatureMutation.ReturnTMismatch)]
    [InlineData(FirstOrDefaultSignatureMutation.PredicateNonBool)]
    [InlineData(FirstOrDefaultSignatureMutation.PredicateBoolAliasKeyword)]
    [InlineData(FirstOrDefaultSignatureMutation.TokenTypeMismatch)]
    [InlineData(FirstOrDefaultSignatureMutation.TokenRefKind)]
    [InlineData(FirstOrDefaultSignatureMutation.ParameterCountTooSmall)]
    [InlineData(FirstOrDefaultSignatureMutation.ParameterCountTooLarge)]
    [InlineData(FirstOrDefaultSignatureMutation.ReturnKindNotTask)]
    [InlineData(FirstOrDefaultSignatureMutation.ChainEntityTMismatch)]
    [InlineData(FirstOrDefaultSignatureMutation.MalformedReceiverType)]
    [InlineData(FirstOrDefaultSignatureMutation.NestedReceiverGeneric)]
    public async Task FirstOrDefaultAsyncSignatureMutationsFailClosed(FirstOrDefaultSignatureMutation mutation)
    {
        var model = new EntityFrameworkQueryModel();
        var operation = ApplyFirstOrDefaultSignatureMutation(ExactFirstOrDefaultOperation(), mutation);
        var result = await model.AnalyzeOperationAsync(
            operation,
            new FrameworkAnalysisContext(Profile, EmptyIndex()),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task QueryFactRetainsExactCertaintyAndFrameworkEvidence()
    {
        var model = new EntityFrameworkQueryModel();
        var result = await model.AnalyzeOperationAsync(
            CreateQueryOperation("SingleOrDefaultAsync", chain: AdmittedChain(), predicate: EqualityPredicate()),
            new FrameworkAnalysisContext(Profile, EmptyIndex()),
            CancellationToken.None);
        var fact = Assert.IsType<EntityFrameworkQueryFact>(Assert.Single(result.Facts));

        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        Assert.NotEmpty(fact.Evidence);
        Assert.All(fact.Evidence, item => Assert.Equal(EvidenceKind.FrameworkModel, item.Kind));
        Assert.All(fact.Evidence, item => Assert.Equal(EntityFrameworkQueryModel.ModelIdValue, item.ProducerId));
        Assert.All(fact.Evidence, item => Assert.Equal(EntityFrameworkQueryModel.ModelVersionValue, item.ProducerVersion));
    }

    [Fact]
    public void IsApplicableRequiresExactEntityFrameworkReference()
    {
        var model = new EntityFrameworkQueryModel();
        Assert.True(model.IsApplicable(new FrameworkDetectionContext(Profile, EmptyIndex(includeEfReference: true))));
        Assert.False(model.IsApplicable(new FrameworkDetectionContext(Profile, EmptyIndex(includeEfReference: false))));
    }

    public enum FirstOrDefaultSignatureMutation
    {
        ReceiverTMismatch,
        PredicateTMismatch,
        ReturnTMismatch,
        PredicateNonBool,
        PredicateBoolAliasKeyword,
        TokenTypeMismatch,
        TokenRefKind,
        ParameterCountTooSmall,
        ParameterCountTooLarge,
        ReturnKindNotTask,
        ChainEntityTMismatch,
        MalformedReceiverType,
        NestedReceiverGeneric,
    }

    private static readonly OperationId PredicateOperationId = new("operation:v1:predicate");
    private static readonly OperationId AsNoTrackingOperation = new("operation:v1:as-no-tracking");
    private static readonly OperationId IncludePartsOperation = new("operation:v1:include-parts");
    private static readonly OperationId IncludeCategoryOperation = new("operation:v1:include-category");

    private static OperationDescriptor CreateQueryOperation(
        string terminalName,
        string? assembly = null,
        FrameworkQueryChainDescriptor? chain = null,
        PredicateShapeDescriptor? predicate = null)
        => new(
            new OperationId($"operation:v1:{terminalName}"),
            new MethodId("method:v1:GetMeaning.Services.GadgetService.GetByIdAsync"),
            "Invocation",
            new DocumentId("document:v1:test"),
            100,
            20,
            [SourceEvidence(terminalName)],
            CertaintyLevel.Exact,
            new FrameworkMethodIdentity(
                assembly ?? EfCoreAssembly,
                QueryableExtensionsType,
                terminalName,
                GenericArity: 1,
                [
                    new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Linq.IQueryable<GetMeaning.Models.Gadget>"),
                    new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Linq.Expressions.Expression<System.Func<GetMeaning.Models.Gadget, System.Boolean>>"),
                    new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Threading.CancellationToken"),
                ],
                ReturnType: "System.Threading.Tasks.Task<GetMeaning.Models.Gadget>",
                AssemblyVersion: EfCoreAssemblyVersion),
            [],
            chain,
            predicate);

    private static OperationDescriptor ExactFirstOrDefaultOperation()
        => CreateQueryOperation("FirstOrDefaultAsync", chain: AdmittedChain(), predicate: EqualityPredicate());

    /// <summary>
    /// Mutates exactly one dimension of the exact FirstOrDefaultAsync declaration so a single
    /// consolidated negative theory proves the model requires the full supported signature rather
    /// than matching the terminal by name, assembly, containing type, and arity alone.
    /// </summary>
    private static OperationDescriptor ApplyFirstOrDefaultSignatureMutation(
        OperationDescriptor exact,
        FirstOrDefaultSignatureMutation mutation)
    {
        var identity = exact.TargetIdentity!;
        var chain = exact.QueryChain!;
        var parameters = identity.Parameters;
        static ParameterIdentityDescriptor Parameter(string type, ParameterRefKind refKind = ParameterRefKind.None)
            => new(refKind, type);

        return mutation switch
        {
            FirstOrDefaultSignatureMutation.ReceiverTMismatch => exact with
            {
                TargetIdentity = identity with
                {
                    Parameters =
                    [
                        Parameter("System.Linq.IQueryable<GetMeaning.Models.Gadget2>"),
                        parameters[1],
                        parameters[2],
                    ],
                },
            },
            FirstOrDefaultSignatureMutation.PredicateTMismatch => exact with
            {
                TargetIdentity = identity with
                {
                    Parameters =
                    [
                        parameters[0],
                        Parameter("System.Linq.Expressions.Expression<System.Func<GetMeaning.Models.Gadget2, System.Boolean>>"),
                        parameters[2],
                    ],
                },
            },
            FirstOrDefaultSignatureMutation.ReturnTMismatch => exact with
            {
                TargetIdentity = identity with { ReturnType = "System.Threading.Tasks.Task<GetMeaning.Models.Gadget2>" },
            },
            FirstOrDefaultSignatureMutation.PredicateNonBool => exact with
            {
                TargetIdentity = identity with
                {
                    Parameters =
                    [
                        parameters[0],
                        Parameter("System.Linq.Expressions.Expression<System.Func<GetMeaning.Models.Gadget, string>>"),
                        parameters[2],
                    ],
                },
            },
            FirstOrDefaultSignatureMutation.PredicateBoolAliasKeyword => exact with
            {
                TargetIdentity = identity with
                {
                    Parameters =
                    [
                        parameters[0],
                        Parameter("System.Linq.Expressions.Expression<System.Func<GetMeaning.Models.Gadget, bool>>"),
                        parameters[2],
                    ],
                },
            },
            FirstOrDefaultSignatureMutation.TokenTypeMismatch => exact with
            {
                TargetIdentity = identity with
                {
                    Parameters =
                    [
                        parameters[0],
                        parameters[1],
                        Parameter("System.Threading.CancellationTokenSource"),
                    ],
                },
            },
            FirstOrDefaultSignatureMutation.TokenRefKind => exact with
            {
                TargetIdentity = identity with
                {
                    Parameters =
                    [
                        parameters[0],
                        parameters[1],
                        Parameter("System.Threading.CancellationToken", ParameterRefKind.In),
                    ],
                },
            },
            FirstOrDefaultSignatureMutation.ParameterCountTooSmall => exact with
            {
                TargetIdentity = identity with { Parameters = [parameters[0], parameters[1]] },
            },
            FirstOrDefaultSignatureMutation.ParameterCountTooLarge => exact with
            {
                TargetIdentity = identity with
                {
                    Parameters =
                    [
                        parameters[0],
                        parameters[1],
                        parameters[2],
                        Parameter("System.Threading.CancellationToken"),
                    ],
                },
            },
            FirstOrDefaultSignatureMutation.ReturnKindNotTask => exact with
            {
                TargetIdentity = identity with { ReturnType = "System.Threading.Tasks.ValueTask<GetMeaning.Models.Gadget>" },
            },
            FirstOrDefaultSignatureMutation.ChainEntityTMismatch => exact with
            {
                QueryChain = chain with { EntityType = "GetMeaning.Models.Gadget2" },
            },
            FirstOrDefaultSignatureMutation.MalformedReceiverType => exact with
            {
                TargetIdentity = identity with
                {
                    Parameters =
                    [
                        Parameter("System.Linq.IQueryable"),
                        parameters[1],
                        parameters[2],
                    ],
                },
            },
            FirstOrDefaultSignatureMutation.NestedReceiverGeneric => exact with
            {
                TargetIdentity = identity with
                {
                    Parameters =
                    [
                        Parameter("System.Linq.IQueryable<System.Collections.Generic.List<GetMeaning.Models.Gadget>>"),
                        parameters[1],
                        parameters[2],
                    ],
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null),
        };
    }

    private static FrameworkQueryChainDescriptor AdmittedChain()
        => new(
            DbSetType,
            DbContextType,
            "Gadgets",
            EntityType,
            [
                ChainStep("AsNoTracking", 1, null),
                ChainStep("Include", 2, "GetMeaning.Models.Gadget.Parts"),
                ChainStep("Include", 2, "GetMeaning.Models.Gadget.Category"),
            ]);

    private static FrameworkChainStepDescriptor ChainStep(string methodName, int arity, string? navigationMember)
        => new(
            methodName switch
            {
                "AsNoTracking" => AsNoTrackingOperation,
                "Include" when navigationMember?.EndsWith("Parts", StringComparison.Ordinal) == true => IncludePartsOperation,
                "Include" => IncludeCategoryOperation,
                _ => new OperationId($"operation:v1:{methodName}"),
            },
            new FrameworkMethodIdentity(
                EfCoreAssembly,
                QueryableExtensionsType,
                methodName,
                arity,
                [],
                ReturnType: "System.Linq.IQueryable<GetMeaning.Models.Gadget>",
                AssemblyVersion: EfCoreAssemblyVersion),
            navigationMember);

    private static PredicateShapeDescriptor EqualityPredicate()
        => new(PredicateShapeKind.EqualityComparison, PredicateOperationId);

    private static EvidenceRef SourceEvidence(string symbol)
        => new(
            new EvidenceId($"evidence:v1:{symbol}"),
            EvidenceKind.Source,
            "Services/GadgetService.cs",
            new SourceRange(
                new DocumentId("document:v1:test"),
                new SourcePosition(10, 0),
                new SourcePosition(10, 30)),
            symbol,
            null,
            CertaintyLevel.Exact);

    private static ProgramIndexSnapshot EmptyIndex(bool includeEfReference = true)
        => new(
            SchemaVersion: 1,
            ProducerVersion: "test",
            Profile,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            includeEfReference
                ? [new ProgramReference(
                    "reference:v1:package|Microsoft.EntityFrameworkCore",
                    new ProjectId("project:v1:test"),
                    ProgramReferenceKind.Package,
                    EfCoreAssembly,
                    "10.0.10",
                    [SourceEvidence("reference")])]
                : [],
            [],
            [],
            [],
            "input-hash",
            "index-fingerprint");
}
