using System.Xml.Linq;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Application.Analysis;

namespace SeqDoc.Analysis.Roslyn.Profiles;

internal static class EvaluatedTargetFrameworkDiscovery
{
    public static string[] Discover(
        CompilationProfileResolutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[]? discovered = null;
        foreach (var projectPath in GetProjectPaths(request.TargetPath).Order(StringComparer.Ordinal))
        {
            using var projectCollection = new ProjectCollection(MsBuildGlobalProperties.CreateForDiscovery(request));
            var project = projectCollection.LoadProject(projectPath);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetFrameworks = project.GetPropertyValue("TargetFrameworks");
                var values = string.IsNullOrWhiteSpace(targetFrameworks)
                    ? [project.GetPropertyValue("TargetFramework")]
                    : targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var evaluated = values
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                if (discovered is not null && !discovered.SequenceEqual(evaluated, StringComparer.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        "The selected solution contains heterogeneous target-framework sets; select one root project.");
                }

                discovered = evaluated;
            }
            finally
            {
                projectCollection.UnloadProject(project);
            }
        }

        return discovered ?? [];
    }

    private static string[] GetProjectPaths(string targetPath)
    {
        var extension = Path.GetExtension(targetPath);
        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return [targetPath];
        }

        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return SolutionFile.Parse(targetPath).ProjectsInOrder
                .Select(project => project.AbsolutePath)
                .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var directory = Path.GetDirectoryName(targetPath)!;
        return XDocument.Load(targetPath).Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFullPath(Path.Combine(directory, path!)))
            .ToArray();
    }
}
