using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Core.Tests;

/// <summary>
/// No persistence layer currently serializes <see cref="ScenarioGraphSet"/>/<see cref="ScenarioRootKind"/>
/// (verified by a repository-wide search), so this enum has no round-trip contract to test against. This
/// is the least-expensive compatibility guard for the additive <see cref="ScenarioRootKind.ServiceOperation"/>
/// member instead: every prior member's underlying numeric value stays exactly what it was before, and the
/// new member is appended last rather than inserted, so any future persistence consumer that serializes
/// this enum by ordinal never silently changes meaning for existing values.
/// </summary>
public sealed class ScenarioRootKindTests
{
    [Fact]
    public void PriorMembersKeepTheirOriginalNumericValues()
    {
        Assert.Equal(0, (int)ScenarioRootKind.HttpEntryPoint);
        Assert.Equal(1, (int)ScenarioRootKind.ConfiguredMethod);
        Assert.Equal(2, (int)ScenarioRootKind.HostedWorker);
    }

    [Fact]
    public void ServiceOperationIsAdditiveAndAppendedLast()
    {
        Assert.Equal(3, (int)ScenarioRootKind.ServiceOperation);
    }
}
