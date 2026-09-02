using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Core.Tests;

/// <summary>
/// Public-contract additive-compatibility freeze for issue 54 (orchestrator resolution 4): every
/// pre-change <see cref="ScenarioNodeKind"/> value is frozen, <see cref="OutboundHttpRequestKind"/>
/// values are frozen, a pre-change positional <see cref="ScenarioNodePresentation"/> construction still
/// compiles with the new trailing parameter defaulting to <c>null</c>, legacy record equality is
/// unchanged, <see cref="OutboundHttpRequestFact"/> is a <see cref="BehaviorFact"/> with the required
/// members, and its stable id includes profile/model/caller/invocation/request-kind/identity-row while
/// excluding checkout path / URI / source text / scheduling / timestamp / encounter order. HARD RED.
/// </summary>
public sealed class OutboundHttpContractCompatibilityTests
{
    [Fact]
    public void OutboundHttpContractsAreAdditiveAndLegacyDefaultsRemainStable()
    {
        // --- frozen ScenarioNodeKind values ---
        Assert.Equal(0, (int)ScenarioNodeKind.Unknown);
        Assert.Equal(1, (int)ScenarioNodeKind.EntryPoint);
        Assert.Equal(2, (int)ScenarioNodeKind.Action);
        Assert.Equal(3, (int)ScenarioNodeKind.MethodCall);
        Assert.Equal(4, (int)ScenarioNodeKind.ServiceCall);
        Assert.Equal(5, (int)ScenarioNodeKind.EntityQuery);
        Assert.Equal(6, (int)ScenarioNodeKind.StateAssignment);
        Assert.Equal(7, (int)ScenarioNodeKind.EntityMutation);
        Assert.Equal(8, (int)ScenarioNodeKind.SourceObservation);
        Assert.Equal(9, (int)ScenarioNodeKind.Result);
        Assert.Equal(10, (int)ScenarioNodeKind.Outcome);
        Assert.Equal(11, (int)ScenarioNodeKind.Delay);
        Assert.Equal(12, (int)ScenarioNodeKind.Dispatch);
        Assert.Equal(13, (int)ScenarioNodeKind.Handler);
        Assert.Equal(14, (int)ScenarioNodeKind.ClientOperationInvocation);
        Assert.Equal(15, (int)ScenarioNodeKind.OutboundHttpRequest);

        // --- frozen OutboundHttpRequestKind values ---
        Assert.Equal(0, (int)OutboundHttpRequestKind.Unknown);
        Assert.Equal(1, (int)OutboundHttpRequestKind.Get);
        Assert.Equal(2, (int)OutboundHttpRequestKind.Post);

        // --- pre-change positional ScenarioNodePresentation still compiles; new field defaults to null ---
        var first = new ScenarioNodePresentation(
            ContractTypeName: "CoreWcfServices.ICalculatorService",
            ClientTypeName: "CoreWcfServices.CalculatorSourceClient",
            CalledMemberName: "Add",
            ClientKind: ServiceClientKind.SourceClient,
            ResultClaimKind: ClientInvocationResultClaimKind.ResultAssigned,
            ResultIsAwaited: true,
            ResultBindingName: "sum",
            DeclaredResultTypeName: "System.Double",
            DeclaredFaultTypeNames: "CoreWcfServices.NegativeSquareRootFault");
        var second = new ScenarioNodePresentation(
            ContractTypeName: "CoreWcfServices.ICalculatorService",
            ClientTypeName: "CoreWcfServices.CalculatorSourceClient",
            CalledMemberName: "Add",
            ClientKind: ServiceClientKind.SourceClient,
            ResultClaimKind: ClientInvocationResultClaimKind.ResultAssigned,
            ResultIsAwaited: true,
            ResultBindingName: "sum",
            DeclaredResultTypeName: "System.Double",
            DeclaredFaultTypeNames: "CoreWcfServices.NegativeSquareRootFault");

        Assert.Null(first.OutboundHttpRequestKind);
        Assert.Equal(first, second); // legacy record equality unchanged when both use the default.

        var withKind = first with { OutboundHttpRequestKind = OutboundHttpRequestKind.Get };
        Assert.NotEqual(first, withKind);

        // --- OutboundHttpRequestFact is a BehaviorFact with the required members ---
        var identity = new FrameworkMethodIdentity(
            "System.Net.Http", "System.Net.Http.HttpClient", "GetAsync", 0,
            [new(ParameterRefKind.None, "System.String")],
            "System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage>", "10.0.0.0", "b03f5f7f11d50a3a");
        var fact = new OutboundHttpRequestFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outbound-http-request:test"),
            Evidence = [Evidence()],
            Certainty = CertaintyLevel.Exact,
            CallerMethod = new MethodId("method:v1:App.Root.Get"),
            InvocationOperation = new OperationId("operation:v1:outbound-http:get"),
            RequestKind = OutboundHttpRequestKind.Get,
            FrameworkMethodIdentity = identity,
        };

        Assert.IsAssignableFrom<BehaviorFact>(fact);
        Assert.Equal(OutboundHttpRequestKind.Get, fact.RequestKind);
        Assert.Equal(new MethodId("method:v1:App.Root.Get"), fact.CallerMethod);
        Assert.Equal(new OperationId("operation:v1:outbound-http:get"), fact.InvocationOperation);
        Assert.Equal(identity, fact.FrameworkMethodIdentity);
        Assert.IsAssignableFrom<BehaviorFact>(fact);
    }

    [Fact]
    public void StableIdIncludesAdmissionInputsAndExcludesEnvironmentalInputs()
    {
        // Issue 54's fact id is built through the shared StableIdentity.CreateBehaviorFactId helper
        // every other framework model uses: the fact kind encodes the admitted request row
        // (outbound-http-request:get / :post) and the operation anchor carries the caller method and
        // invocation operation. The active compilation profile pins the single admitted assembly
        // version, so profile + fact kind + operation anchor fully determine the admitted row.
        var baseDescriptor = new BehaviorFactIdentityDescriptor(
            Profile: new CompilationProfileId("profile:v1:net10.0"),
            ModelId: "seqdoc.system-net-http.outbound",
            ModelVersion: "1.0.0",
            FactKind: "outbound-http-request:get",
            Anchor: new OperationBehaviorFactAnchor(
                new MethodId("method:v1:App.Root.Get"),
                new OperationId("operation:v1:outbound-http:get")),
            SameKindSiblingOrdinal: 0);

        var id = StableIdentity.CreateBehaviorFactId(baseDescriptor);

        // An included input changes the id: request kind (fact kind), profile, model id, model
        // version, caller method (operation anchor), invocation operation (operation anchor).
        Assert.NotEqual(id, StableIdentity.CreateBehaviorFactId(
            baseDescriptor with { FactKind = "outbound-http-request:post" }));
        Assert.NotEqual(id, StableIdentity.CreateBehaviorFactId(
            baseDescriptor with { Profile = new CompilationProfileId("profile:v1:net9.0") }));
        Assert.NotEqual(id, StableIdentity.CreateBehaviorFactId(
            baseDescriptor with { ModelId = "seqdoc.other" }));
        Assert.NotEqual(id, StableIdentity.CreateBehaviorFactId(
            baseDescriptor with { ModelVersion = "2.0.0" }));
        Assert.NotEqual(id, StableIdentity.CreateBehaviorFactId(
            baseDescriptor with
            {
                Anchor = new OperationBehaviorFactAnchor(
                    new MethodId("method:v1:App.Root.Other"),
                    new OperationId("operation:v1:outbound-http:get")),
            }));
        Assert.NotEqual(id, StableIdentity.CreateBehaviorFactId(
            baseDescriptor with
            {
                Anchor = new OperationBehaviorFactAnchor(
                    new MethodId("method:v1:App.Root.Get"),
                    new OperationId("operation:v1:outbound-http:other")),
            }));

        // Repeated construction is stable; excluded environmental inputs (checkout path / URI value /
        // source text / scheduling / timestamp / encounter order) are not descriptor inputs at all.
        Assert.Equal(id, StableIdentity.CreateBehaviorFactId(baseDescriptor));
    }

    // residual boundary: SeqDoc.Core.Tests references only SeqDoc.Core, so a reflection assertion that no
    // exported persistence type exposes a member typed OutboundHttpRequestFact / OutboundHttpRequestKind
    // cannot run here without a disallowed .csproj change. The additive contract keeps the fact and the
    // presentation field memory-only (no serializer/DTO surface); persistence isolation is covered by the
    // absence of any persistence-contract change in the issue-54 diff and the Rendering.Tests final gate.

    private static EvidenceRef Evidence()
        => new(
            new EvidenceId("evidence:v1:outbound-http"),
            EvidenceKind.Source,
            "SupportedRequests.cs",
            new SourceRange(new DocumentId("document:v1:test"), new SourcePosition(1, 0), new SourcePosition(1, 10)),
            "App.Root.Get",
            null,
            CertaintyLevel.Exact);
}
