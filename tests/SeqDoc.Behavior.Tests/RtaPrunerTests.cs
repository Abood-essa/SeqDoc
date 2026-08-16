using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using Xunit;

namespace SeqDoc.Behavior.Tests;

public sealed class RtaPrunerTests
{
    private static readonly CompilationProfile Profile = CompilationProfile.Create("Rta.csproj", "Release", "net10.0");
    private static readonly SymbolId RootType = new("symbol:v1:root");
    private static readonly SymbolId ReachableType = new("symbol:v1:reachable");
    private static readonly SymbolId UnreachableType = new("symbol:v1:unreachable");

    [Fact]
    public void ClosedWorldPruningRemovesCandidatesOnUnreachableTypes()
    {
        var reachableMethod = new MethodId("method:v1:reachable.run");
        var unreachableMethod = new MethodId("method:v1:unreachable.run");
        var index = CreateIndex(reachableMethod, unreachableMethod);
        var candidates = ImmutableArray.Create(unreachableMethod, reachableMethod);

        var pruned = RtaPruner.Prune(
            index,
            candidates,
            ImmutableArray.Create(ReachableType),
            assumeClosedWorld: true);

        Assert.Equal(reachableMethod, Assert.Single(pruned));
    }

    [Fact]
    public void OpenWorldDoesNotRemoveCandidates()
    {
        var reachableMethod = new MethodId("method:v1:reachable.run");
        var unreachableMethod = new MethodId("method:v1:unreachable.run");
        var index = CreateIndex(reachableMethod, unreachableMethod);
        var candidates = ImmutableArray.Create(unreachableMethod, reachableMethod);

        var pruned = RtaPruner.Prune(
            index,
            candidates,
            ImmutableArray.Create(RootType),
            assumeClosedWorld: false);

        Assert.Equal(2, pruned.Length);
    }

    [Fact]
    public void EmptyRootSetIsRejected()
    {
        var index = CreateIndex(new MethodId("method:v1:a"), new MethodId("method:v1:b"));
        Assert.Throws<ArgumentException>(() => RtaPruner.Prune(
            index,
            ImmutableArray.Create(new MethodId("method:v1:a")),
            [],
            assumeClosedWorld: true));
    }

    private static ProgramIndexSnapshot CreateIndex(MethodId reachableMethod, MethodId unreachableMethod) =>
        new(
            1,
            "test",
            Profile,
            [],
            [],
            [],
            ImmutableArray.Create(
                CreateType(RootType, null),
                CreateType(ReachableType, RootType),
                CreateType(UnreachableType, null)),
            [],
            ImmutableArray.Create(
                CreateMethod(reachableMethod, ReachableType),
                CreateMethod(unreachableMethod, UnreachableType)),
            [],
            [],
            [],
            [],
            [],
            "manifest",
            "fingerprint");

    private static ProgramType CreateType(SymbolId id, SymbolId? baseType) =>
        new(id, new ProjectId("project:v1:test"), new SymbolId("symbol:v1:ns"), "Type", ProgramTypeKind.Class, baseType, [], "sig", []);

    private static ProgramMethod CreateMethod(MethodId id, SymbolId containingType) =>
        new(
            id,
            new SymbolId($"symbol:v1:{id.Value}"),
            containingType,
            "Run",
            "Run",
            [],
            "System.Void",
            "sig",
            null,
            []);
}
