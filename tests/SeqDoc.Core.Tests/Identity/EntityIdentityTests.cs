using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Core.Tests.Identity;

public sealed class EntityIdentityTests
{
    private static readonly ProjectId Project = new("project:v1:test");

    [Fact]
    public void GeneratedDocumentRequiresStableGeneratorIdentity()
    {
        var descriptor = new DocumentIdentityDescriptor(
            Project,
            DocumentIdentityKind.GeneratedSource,
            "generated/Routes.g.cs");

        Assert.Throws<ArgumentException>(() => StableIdentity.CreateDocumentId(descriptor));
    }

    [Fact]
    public void GeneratedDocumentIdentityDoesNotUseTemporaryOutputPath()
    {
        var first = StableIdentity.CreateDocumentId(new DocumentIdentityDescriptor(
            Project,
            DocumentIdentityKind.GeneratedSource,
            "obj/Debug/net10.0/generated/Routes.g.cs",
            "Company.RouteGenerator, Version=1.0.0.0",
            "Routes.g.cs"));
        var second = StableIdentity.CreateDocumentId(new DocumentIdentityDescriptor(
            Project,
            DocumentIdentityKind.GeneratedSource,
            "obj/Release/net10.0/different/Routes.g.cs",
            "Company.RouteGenerator, Version=1.0.0.0",
            "Routes.g.cs"));

        Assert.Equal(first, second);
        Assert.Equal(
            "document:v1:08bc7bdd4f8dcaeb3f80f0c06e5c793c6fd67b1855aeeca1610b46f9514ddea5",
            first.Value);
        Assert.DoesNotContain("obj", first.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceDocumentRejectsGeneratorOnlyMetadata()
    {
        var descriptor = new DocumentIdentityDescriptor(
            Project,
            DocumentIdentityKind.Source,
            "src/App.cs",
            "Generator",
            "App.g.cs");

        Assert.Throws<ArgumentException>(() => StableIdentity.CreateDocumentId(descriptor));
    }

    [Fact]
    public void OverloadParameterTypeChangesSymbolAndMethodIdentity()
    {
        var integerMethod = CreateMethodDescriptor("System.Int32");
        var stringMethod = CreateMethodDescriptor("System.String");

        Assert.NotEqual(StableIdentity.CreateSymbolId(integerMethod), StableIdentity.CreateSymbolId(stringMethod));
        Assert.NotEqual(StableIdentity.CreateMethodId(integerMethod), StableIdentity.CreateMethodId(stringMethod));
        Assert.Equal(
            "symbol:v1:f7b10f201f7a6c3f53ca0d4f9b615821f0c18d3b929d51329d8108d5f2c3ec7f",
            StableIdentity.CreateSymbolId(integerMethod).Value);
        Assert.Equal(
            "method:v1:f7b10f201f7a6c3f53ca0d4f9b615821f0c18d3b929d51329d8108d5f2c3ec7f",
            StableIdentity.CreateMethodId(integerMethod).Value);
    }

    [Fact]
    public void GenericArityChangesSymbolIdentity()
    {
        var first = CreateMethodDescriptor("System.Int32");

        Assert.NotEqual(
            StableIdentity.CreateSymbolId(first),
            StableIdentity.CreateSymbolId(first with { GenericArity = 1 }));
    }

    [Fact]
    public void EmptyOptionalIdentityValueIsRejectedRatherThanAliasedWithNull()
    {
        var valid = CreateMethodDescriptor("System.Int32");
        Assert.NotEqual(default, StableIdentity.CreateSymbolId(valid));

        Assert.Throws<ArgumentException>(() => StableIdentity.CreateSymbolId(valid with
        {
            ExplicitInterfaceIdentity = string.Empty,
        }));
    }

    [Fact]
    public void OrdinaryMethodReturnTypeDoesNotChangeIdentity()
    {
        var first = CreateMethodDescriptor("System.Int32");
        var second = first with { ReturnType = "System.String" };

        Assert.Equal(StableIdentity.CreateMethodId(first), StableIdentity.CreateMethodId(second));
    }

    [Fact]
    public void ConversionReturnTypeCanDisambiguateIdentity()
    {
        var first = CreateMethodDescriptor("System.Int32") with { IncludeReturnTypeInIdentity = true };
        var second = first with { ReturnType = "System.String" };

        Assert.NotEqual(StableIdentity.CreateMethodId(first), StableIdentity.CreateMethodId(second));
    }

    [Fact]
    public void ExplicitInterfaceIdentityChangesSymbolIdentity()
    {
        var first = CreateMethodDescriptor("System.Int32") with
        {
            ExplicitInterfaceIdentity = "Company.IFirst.Run",
        };
        var second = first with { ExplicitInterfaceIdentity = "Company.ISecond.Run" };

        Assert.NotEqual(StableIdentity.CreateSymbolId(first), StableIdentity.CreateSymbolId(second));
    }

    [Fact]
    public void SourceEditMayChangeRevisionLocalOperationIdentity()
    {
        var first = new OperationIdentityDescriptor(
            new DocumentId("document:v1:test"),
            new MethodId("method:v1:test"),
            "Invocation",
            120,
            8,
            0);

        var firstId = StableIdentity.CreateOperationId(first);
        Assert.Equal(
            "operation:v1:43453353fb258f8e259babb8d8793f9b806c72d2c49fa51eebfcff6ad0b649c1",
            firstId.Value);
        Assert.NotEqual(
            firstId,
            StableIdentity.CreateOperationId(first with { SourceStart = 121 }));
    }

    [Fact]
    public void EvidenceAndDiagnosticDescriptorsProduceTypeSpecificIds()
    {
        var evidence = StableIdentity.CreateEvidenceId(new EvidenceIdentityDescriptor(
            EvidenceKind.Source,
            "src/App.cs",
            new DocumentId("document:v1:test"),
            10,
            5,
            "Company.App.Run",
            CertaintyLevel.Exact));
        var diagnostic = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            "SEQTEST001",
            AnalysisStage.BaselineIndex,
            new CompilationProfileId("profile:v1:test"),
            "method:v1:test",
            0));

        Assert.StartsWith("evidence:v1:", evidence.Value, StringComparison.Ordinal);
        Assert.StartsWith("diagnostic:v1:", diagnostic.Value, StringComparison.Ordinal);
        Assert.Equal(
            "evidence:v1:7c9122c2f359c6195eb811d4d879961926240374ab3bc0278c1ccb78317af127",
            evidence.Value);
        Assert.Equal(
            "diagnostic:v1:22b6f795868bfc39f2310701bd8583f72d72ab74d457bbd327d694c5a50db395",
            diagnostic.Value);
        Assert.NotEqual(evidence.Value, diagnostic.Value);
    }

    [Fact]
    public void DetailAwareEvidenceUsesVersionTwoWithoutReinterpretingVersionOne()
    {
        var first = new EvidenceIdentityDescriptor(
            EvidenceKind.Configuration,
            "src/App.csproj",
            null,
            null,
            null,
            null,
            CertaintyLevel.Exact,
            Detail: "project");
        var second = first with { Detail = "assembly" };

        Assert.Equal(StableIdentity.CreateEvidenceId(first), StableIdentity.CreateEvidenceId(second));
        Assert.StartsWith("evidence:v2:", StableIdentity.CreateEvidenceIdV2(first).Value, StringComparison.Ordinal);
        Assert.NotEqual(StableIdentity.CreateEvidenceIdV2(first), StableIdentity.CreateEvidenceIdV2(second));
    }

    [Fact]
    public void ProfileIdentityIsIndependentOfParallelScheduling()
    {
        var ids = Enumerable.Range(0, 100)
            .AsParallel()
            .Select(_ => CompilationProfile.Create(
                "src/App/App.csproj",
                "Release",
                "net10.0",
                msBuildProperties: [KeyValuePair.Create("Feature", "Enabled")]).Id)
            .Distinct()
            .ToArray();

        Assert.Single(ids);
    }

    [Fact]
    public void FullIdentitySetIsIndependentOfParallelScheduling()
    {
        var expected = CreateIdentitySet();
        var sets = Enumerable.Range(0, 50)
            .AsParallel()
            .Select(_ => CreateIdentitySet())
            .ToArray();

        Assert.All(sets, set => Assert.Equal(expected, set));
    }

    [Fact]
    public void BehaviorOperationIdentityIsDeterministicAndRevisionLocal()
    {
        var descriptor = new BehaviorOperationIdentityDescriptor(
            new MethodId("method:v1:test"),
            "Invocation",
            1,
            4,
            new DocumentId("document:v1:test"),
            100,
            12,
            0);

        var first = StableIdentity.CreateBehaviorOperationId(descriptor);
        Assert.Equal(first, StableIdentity.CreateBehaviorOperationId(descriptor));
        Assert.StartsWith("behavior-operation:v1:", first.Value, StringComparison.Ordinal);
        Assert.NotEqual(
            first,
            StableIdentity.CreateBehaviorOperationId(descriptor with { EvaluationOrdinal = 5 }));
        Assert.NotEqual(
            first,
            StableIdentity.CreateBehaviorOperationId(descriptor with { SourceStart = 101 }));
    }

    [Fact]
    public void ImplicitBehaviorOperationRequiresNoDocumentOrSpan()
    {
        var descriptor = new BehaviorOperationIdentityDescriptor(
            new MethodId("method:v1:test"),
            "FlowCapture",
            2,
            0,
            null,
            0,
            0,
            0);

        var id = StableIdentity.CreateBehaviorOperationId(descriptor);
        Assert.StartsWith("behavior-operation:v1:", id.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void BehaviorOperationWithDocumentRequiresNonEmptySpan()
    {
        var descriptor = new BehaviorOperationIdentityDescriptor(
            new MethodId("method:v1:test"),
            "FlowCapture",
            2,
            0,
            new DocumentId("document:v1:test"),
            0,
            0,
            0);

        Assert.Throws<ArgumentException>(() => StableIdentity.CreateBehaviorOperationId(descriptor));
    }

    [Fact]
    public void FlowRegionIdentityIsDeterministicAndKindSensitive()
    {
        var first = StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(
            new MethodId("method:v1:test"),
            "Finally",
            1));
        Assert.Equal(
            first,
            StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(
                new MethodId("method:v1:test"),
                "Finally",
                1)));
        Assert.StartsWith("flow-region:v1:", first.Value, StringComparison.Ordinal);
        Assert.NotEqual(
            first,
            StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(
                new MethodId("method:v1:test"),
                "Try",
                1)));
    }

    private static SymbolIdentityDescriptor CreateMethodDescriptor(string parameterType)
    {
        return new SymbolIdentityDescriptor(
            Project,
            "Company.App, Version=1.0.0.0, PublicKeyToken=null",
            "Company.Services.OrderService",
            SymbolIdentityKind.Method,
            "Run",
            0,
            null,
            ImmutableArray.Create(new ParameterIdentityDescriptor(ParameterRefKind.None, parameterType)),
            "System.Threading.Tasks.Task");
    }

    private static string[] CreateIdentitySet()
    {
        var profile = CompilationProfile.Create("src/App/App.csproj", "Release", "net10.0");
        var project = StableIdentity.CreateProjectId(profile.Id, "src/App/App.csproj");
        var document = StableIdentity.CreateDocumentId(new DocumentIdentityDescriptor(
            project,
            DocumentIdentityKind.Source,
            "src/App.cs"));
        var methodDescriptor = CreateMethodDescriptor("System.Int32") with { Project = project };
        var method = StableIdentity.CreateMethodId(methodDescriptor);
        var evidence = StableIdentity.CreateEvidenceId(new EvidenceIdentityDescriptor(
            EvidenceKind.Source,
            "src/App.cs",
            document,
            10,
            5,
            "Company.App.Run",
            CertaintyLevel.Exact));
        var diagnostic = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            "SEQTEST001",
            AnalysisStage.BaselineIndex,
            profile.Id,
            method.Value,
            0));

        return
        [
            profile.Id.Value,
            project.Value,
            document.Value,
            StableIdentity.CreateSymbolId(methodDescriptor).Value,
            method.Value,
            StableIdentity.CreateOperationId(new OperationIdentityDescriptor(
                document,
                method,
                "Invocation",
                10,
                5,
                0)).Value,
            evidence.Value,
            diagnostic.Value,
        ];
    }
}
