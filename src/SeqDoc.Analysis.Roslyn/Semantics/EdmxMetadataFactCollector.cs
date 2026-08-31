using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using SeqDoc.Analysis.Roslyn.Frameworks;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;

namespace SeqDoc.Analysis.Roslyn.Semantics;

internal static class EdmxMetadataFactCollector
{
    public static void AddEvaluatedItems(LoadedProject project, CompilationProfile profile, string repositoryRoot, FrameworkAnalysisRequestCollector request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(project.Project.FilePath))
        {
            return;
        }

        var globals = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Configuration"] = profile.Configuration,
            ["TargetFramework"] = profile.TargetFramework,
        };
        using var collection = new ProjectCollection(globals);
        Microsoft.Build.Evaluation.Project evaluated;
        try
        {
            evaluated = new Microsoft.Build.Evaluation.Project(project.Project.FilePath, globals, null, collection);
        }
        catch (Microsoft.Build.Exceptions.InvalidProjectFileException)
        {
            // An imported project may be unavailable when a source tree is physically relocated.
            // Evaluation is supplementary metadata evidence; withhold it rather than failing the
            // compiler extraction. Cancellation is intentionally not caught here.
            return;
        }
        var items = evaluated.Items
            .Where(item => item.ItemType is "EmbeddedResource" or "EntityDeploy")
            .Where(item => item.EvaluatedInclude.EndsWith(".edmx", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.Project.FilePath)!, item.EvaluatedInclude)), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = group.Key;
            var relative = Path.GetRelativePath(repositoryRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                continue;
            }

            byte[] bytes;
            XDocument? document;
            try
            {
                bytes = File.ReadAllBytes(fullPath);
                cancellationToken.ThrowIfCancellationRequested();
                document = XDocument.Parse(Encoding.UTF8.GetString(bytes), LoadOptions.PreserveWhitespace);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }
            var root = document.Root;
            XNamespace edmx = "http://schemas.microsoft.com/ado/2008/10/edmx";
            if (root?.Name != edmx + "Edmx" || (string?)root.Attribute("Version") != "2.0")
            {
                continue;
            }

            const string csdl = "http://schemas.microsoft.com/ado/2009/11/edm";
            const string ssdl = "http://schemas.microsoft.com/ado/2009/11/edm/ssdl";
            bool import = root.Descendants().Any(x => x.Name == XName.Get("FunctionImport", csdl));
            bool function = root.Descendants().Any(x => x.Name == XName.Get("Function", ssdl));
            var evidence = new EvidenceRef(new EvidenceId("evidence:edmx:" + relative), EvidenceKind.Source, relative,
                new SourceRange(new DocumentId("document:edmx:" + relative), new SourcePosition(0, 0), new SourcePosition(0, 1)), "EDMX", "evaluated EDMX item", CertaintyLevel.Exact);
            cancellationToken.ThrowIfCancellationRequested();
            var fingerprint = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            request.AddOperation(new OperationDescriptor(new OperationId($"operation:edmx:{project.StableId.Value}:{relative}:{fingerprint}"),
                new MethodId("metadata:edmx:" + project.StableId.Value), "EdmxMetadata", evidence.Range!.Document, 0, bytes.Length, [evidence], CertaintyLevel.Exact,
                ConstantArguments: [new CompilerProvenArgument(0, "System.String", project.StableId.Value), new CompilerProvenArgument(1, "System.String", relative), new CompilerProvenArgument(2, "System.String", fingerprint), new CompilerProvenArgument(3, "System.Boolean", import ? "true" : "false"), new CompilerProvenArgument(4, "System.Boolean", function ? "true" : "false")]));
        }
    }
}
