using System.Diagnostics;
using System.Text.Json;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class ProgramIndexBuilderTests
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Fact]
    public async Task GeneratedAndPartialFixtureProducesEvidenceBackedIndex()
    {
        var request = CreateFixtureRequest("GeneratedAndPartialSource");
        var result = await new RoslynProgramIndexBuilder().BuildAsync(request, CancellationToken.None);

        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}\n{diagnostic.InternalDetail}")));
        var index = Assert.IsType<ProgramIndexSnapshot>(result.Value);
        Assert.Contains(index.Documents, document => document.Origin == DocumentOrigin.Source);
        Assert.Contains(index.Documents, document => document.Origin == DocumentOrigin.LinkedSource);
        Assert.Contains(index.Documents, document => document.Origin == DocumentOrigin.GeneratedSource);
        Assert.DoesNotContain(index.Documents, document => document.LogicalPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase));

        var partialType = Assert.Single(index.Types, type => type.MetadataName.EndsWith(".ReservationMatcher", StringComparison.Ordinal));
        Assert.True(partialType.Evidence.Length >= 2);
        Assert.Contains(index.Members, member => member.ContainingType == partialType.Id && member.Kind == ProgramMemberKind.Field);
        Assert.Contains(index.Members, member => member.ContainingType == partialType.Id && member.Kind == ProgramMemberKind.Property);
        Assert.Contains(index.Members, member => member.ContainingType == partialType.Id && member.Kind == ProgramMemberKind.Event);
        Assert.Contains(index.Attributes, attribute => attribute.AttributeType.EndsWith("ObsoleteAttribute", StringComparison.Ordinal));
        var repeatedAttributes = index.Attributes
            .Where(attribute => attribute.AttributeType.EndsWith("SuppressMessageAttribute", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, repeatedAttributes.Length);
        Assert.Equal(2, repeatedAttributes.Select(attribute => attribute.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(index.Invocations, invocation => invocation.BoundTarget is not null && invocation.Certainty == CertaintyLevel.Exact);
        Assert.Contains(index.Invocations, invocation => invocation.DisplayTarget == "GeneratedAndPartialSource.IReservationMatcher.IsMatch(string)" && invocation.BoundTarget is not null && invocation.Certainty == CertaintyLevel.Conservative);
        Assert.Contains(index.Invocations, invocation => invocation.DisplayTarget == "GeneratedAndPartialSource.ReservationMatcher.ReservationMatcher()" && invocation.BoundTarget is not null);
        var initializerInvocation = Assert.Single(index.Invocations, invocation => invocation.DisplayTarget == "string.Concat(string?, string?)");
        Assert.Contains(index.Methods, method => method.Id == initializerInvocation.ContainingMethod);
        var partialMethod = Assert.Single(index.Methods, method => method.Name == "ReservationRegex");
        Assert.True(partialMethod.Evidence.Length >= 2);
        Assert.NotNull(partialMethod.BodyFingerprint);
        Assert.All(index.Projects, project => Assert.Equal(64, project.BuildFingerprint.Length));
        Assert.Equal(64, index.InputManifestHash.Length);
        Assert.Equal(64, index.IndexFingerprint.Length);

        var projection = CreateGoldenProjection(index);
        var goldenPath = Path.Combine(FindRepositoryRoot(), "tests", "SeqDoc.Analysis.Tests", "Golden", "generated-partial-index.json");
        var expected = await File.ReadAllTextAsync(goldenPath);
        Assert.Equal(NormalizeLines(expected), NormalizeLines(projection));
    }

    [Fact]
    public async Task ConstructedInterfacesUseOneDeclarationEdge()
    {
        var result = await new RoslynProgramIndexBuilder().BuildAsync(
            CreateFixtureRequest("DuplicateConstructedInterfaces"),
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.Succeeded, result.Outcome);
        var index = Assert.IsType<ProgramIndexSnapshot>(result.Value);
        var contract = Assert.Single(index.Types, type =>
            type.MetadataName.EndsWith(".IContract`1", StringComparison.Ordinal));
        var implementation = Assert.Single(index.Types, type =>
            type.MetadataName.EndsWith(".MultipleContracts", StringComparison.Ordinal));
        Assert.Equal(contract.Id, Assert.Single(implementation.Interfaces));
    }

    [Fact]
    public async Task PhysicalCheckoutRelocationDoesNotChangeIndexIdentity()
    {
        var source = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "PassA", "RelocatableIdentity");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"seqdoc-relocation-{Guid.NewGuid():N}");
        var firstRoot = Path.Combine(temporaryDirectory, "first");
        var secondRoot = Path.Combine(temporaryDirectory, "second");
        try
        {
            CopyFixture(source, firstRoot);
            CopyFixture(source, secondRoot);
            await RestoreAsync(firstRoot);
            await RestoreAsync(secondRoot);

            var first = await BuildRelocatedAsync(firstRoot);
            var second = await BuildRelocatedAsync(secondRoot);

            Assert.Equal(first.IndexFingerprint, second.IndexFingerprint);
            Assert.Equal(first.InputManifestHash, second.InputManifestHash);
            Assert.Equal(first.Projects.Select(item => item.Id), second.Projects.Select(item => item.Id));
            Assert.Equal(first.Documents.Select(item => item.Id), second.Documents.Select(item => item.Id));
            Assert.Equal(first.Types.Select(item => item.Id), second.Types.Select(item => item.Id));
            Assert.Equal(first.Methods.Select(item => item.Id), second.Methods.Select(item => item.Id));
            Assert.DoesNotContain(firstRoot, JsonSerializer.Serialize(first), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secondRoot, JsonSerializer.Serialize(second), StringComparison.OrdinalIgnoreCase);

            var secondSourcePath = Path.Combine(secondRoot, "ReservationService.cs");
            var secondSource = await File.ReadAllTextAsync(secondSourcePath);
            await File.WriteAllTextAsync(
                secondSourcePath,
                secondSource.Replace("public sealed class ReservationService", "public class ReservationService", StringComparison.Ordinal));
            var changed = await BuildRelocatedAsync(secondRoot);
            var originalType = first.Types.Single(type => type.MetadataName == "RelocatableIdentity.ReservationService");
            var changedType = changed.Types.Single(type => type.MetadataName == "RelocatableIdentity.ReservationService");
            Assert.Equal(originalType.Id, changedType.Id);
            Assert.NotEqual(originalType.SignatureFingerprint, changedType.SignatureFingerprint);
            Assert.NotEqual(first.IndexFingerprint, changed.IndexFingerprint);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TicketReservationLoadsThroughBaselineIndexerWhenAvailable()
    {
        const string root = "samples/Provided/TicketReservation-Solution";
        var target = Path.Combine(root, "TicketReservation.sln");
        if (!File.Exists(target))
        {
            return;
        }

        var profile = CompilationProfile.Create("TicketReservation.sln", "Release", "net10.0");
        var result = await new RoslynProgramIndexBuilder().BuildAsync(
            new CompilationAnalysisRequest(root, target, profile),
            CancellationToken.None);

        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}\n{diagnostic.InternalDetail}")));
        var index = Assert.IsType<ProgramIndexSnapshot>(result.Value);
        Assert.NotEmpty(index.Projects);
        Assert.NotEmpty(index.Documents);
        Assert.NotEmpty(index.Types);
        Assert.NotEmpty(index.Methods);
        Assert.NotEmpty(index.Invocations);
        Assert.Contains(index.References, reference => reference.Kind == ProgramReferenceKind.Package);
        Assert.Contains(index.InventoryMarkers, marker => marker.Kind == InventoryMarkerKind.EntryPointCandidate);
        Assert.Contains(index.InventoryMarkers, marker => marker.Kind == InventoryMarkerKind.FrameworkConfigurationCandidate);
    }

    private static CompilationAnalysisRequest CreateFixtureRequest(string name)
    {
        var root = FindRepositoryRoot();
        var relativePath = $"tests/fixtures/PassA/{name}/{name}.csproj";
        return new CompilationAnalysisRequest(
            root,
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(relativePath, "Release", "net10.0"));
    }

    private static async Task<ProgramIndexSnapshot> BuildRelocatedAsync(string root)
    {
        const string relativePath = "RelocatableIdentity.csproj";
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, relativePath),
            CompilationProfile.Create(relativePath, "Release", "net10.0"));
        var result = await new RoslynProgramIndexBuilder().BuildAsync(request, CancellationToken.None);
        Assert.Equal(ApplicationOutcome.Succeeded, result.Outcome);
        return Assert.IsType<ProgramIndexSnapshot>(result.Value);
    }

    private static void CopyFixture(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (Path.GetFileName(file) != "packages.lock.json")
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }
    }

    private static async Task RestoreAsync(string root)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "restore RelocatableIdentity.csproj --nologo",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"{await output}\n{await error}");
    }

    private static string CreateGoldenProjection(ProgramIndexSnapshot index)
    {
        var projection = new
        {
            index.SchemaVersion,
            ProjectCount = index.Projects.Length,
            Documents = index.Documents.OrderBy(document => document.LogicalPath, StringComparer.Ordinal).Select(document => new { document.LogicalPath, Origin = document.Origin.ToString() }),
            Namespaces = index.Namespaces.Select(item => item.Name).Order(StringComparer.Ordinal),
            UserTypes = index.Types.Where(item => item.MetadataName.StartsWith("GeneratedAndPartialSource.", StringComparison.Ordinal)).OrderBy(item => item.MetadataName, StringComparer.Ordinal).Select(item => new { item.MetadataName, Kind = item.Kind.ToString(), Declarations = item.Evidence.Length }),
            UserMembers = index.Members.Where(item => item.ContainingType == index.Types.Single(type => type.MetadataName == "GeneratedAndPartialSource.ReservationMatcher").Id).OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Kind).Select(item => new { item.Name, Kind = item.Kind.ToString(), item.FullyQualifiedType }),
            UserMethods = index.Methods.Select(item => item.DisplaySignature).Where(item => item.StartsWith("GeneratedAndPartialSource.", StringComparison.Ordinal)).Order(StringComparer.Ordinal),
            Attributes = index.Attributes.Select(item => item.AttributeType).Distinct().Order(StringComparer.Ordinal),
            ReferenceKinds = index.References.Select(item => item.Kind.ToString()).Distinct().Order(StringComparer.Ordinal),
            SelectedInvocationTargets = index.Invocations.Select(item => item.DisplayTarget).Where(item => item.StartsWith("GeneratedAndPartialSource.", StringComparison.Ordinal) || item is "System.EventHandler.Invoke(object?, System.EventArgs)" or "System.Text.RegularExpressions.Regex.IsMatch(string)").Distinct().Order(StringComparer.Ordinal),
            MarkerKinds = index.InventoryMarkers.Select(item => item.Kind.ToString()).Distinct().Order(StringComparer.Ordinal),
            TypeCount = index.Types.Length,
            GeneratedDocumentCount = index.Documents.Count(document => document.Origin == DocumentOrigin.GeneratedSource),
        };
        return JsonSerializer.Serialize(projection, IndentedJson);
    }

    private static string NormalizeLines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
