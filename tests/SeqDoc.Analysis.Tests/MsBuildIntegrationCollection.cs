using Xunit;

namespace SeqDoc.Analysis.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MsBuildIntegrationGroup
{
    public const string Name = "MSBuild integration";
}
