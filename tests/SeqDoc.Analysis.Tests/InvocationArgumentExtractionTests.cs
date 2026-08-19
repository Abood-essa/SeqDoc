using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class InvocationArgumentExtractionTests
{
    [Fact]
    public async Task CompilerBoundArgumentMappingsPreserveOrdinalsOptionalGapsTypedNullAndLiteralShapes()
    {
        var root = FindRepositoryRoot();
        var relativePath = "tests/fixtures/PassB/InvocationArgumentExtraction/InvocationArgumentExtraction.csproj";
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root,
                Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                CompilationProfile.Create(relativePath, "Release", "net10.0")), CancellationToken.None);

        Assert.True(result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.TechnicalCause}")));
        var extraction = Assert.IsType<ProfileAnalysisExtraction>(result.Value);

        var complete = Invocation(extraction, "Complete");
        Assert.Equal([0, 1, 2, 3], complete.Invocation!.ArgumentMappings.Select(item => item.ParameterOrdinal));
        Assert.All(complete.Invocation.ArgumentMappings, item => Assert.True(item.IsMappingComplete));
        Assert.Equal(["7", "alpha", "null", "line\n\"quote"],
            complete.Invocation.ArgumentMappings.Select(item => Operation(extraction, item.Operation).ConstantValue));

        var omitted = Invocation(extraction, "OmittedIntermediateOptional");
        Assert.Equal([0, 3], omitted.Invocation!.ArgumentMappings.Select(item => item.ParameterOrdinal));
        Assert.All(omitted.Invocation.ArgumentMappings, item => Assert.True(item.IsMappingComplete));

        var nulls = Invocation(extraction, "NullAndSensitive");
        var nullArgument = Assert.Single(nulls.Invocation!.ArgumentMappings, item => item.ParameterOrdinal == 1);
        var nullOperation = Operation(extraction, nullArgument.Operation);
        Assert.True(nullArgument.IsMappingComplete);
        Assert.Equal("System.String", nullOperation.TypeDescriptor);
        Assert.True(nullOperation.HasConstantValue);
        Assert.Null(nullOperation.ConstantValue);
        Assert.Equal("null", Operation(extraction,
            Assert.Single(nulls.Invocation.ArgumentMappings, item => item.ParameterOrdinal == 2).Operation).ConstantValue);
        Assert.Equal("AKIA" + "TEST000000000000", Operation(extraction,
            Assert.Single(nulls.Invocation.ArgumentMappings, item => item.ParameterOrdinal == 3).Operation).ConstantValue);
    }

    private static ExtractedOperation Operation(ProfileAnalysisExtraction extraction, SeqDoc.Core.Identity.OperationId id) =>
        Assert.Single(extraction.BehaviorInput.Methods.SelectMany(body => body.Operations), operation => operation.Id == id);

    private static ExtractedOperation Invocation(ProfileAnalysisExtraction extraction, string methodName) =>
        Assert.Single(extraction.BehaviorInput.Methods
            .Where(body => extraction.ProgramIndex.Methods.Any(method => method.Id == body.Method && method.Name == methodName))
            .SelectMany(body => body.Operations), operation => operation.Invocation is not null);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
