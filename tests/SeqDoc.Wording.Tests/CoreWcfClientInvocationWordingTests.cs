using System.Collections.Immutable;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Wording.Tests;

/// <summary>
/// Observable-proof test for the client-invocation claim-transition table added on top of issue #5/#7's
/// client-boundary facts: a <see cref="ScenarioNodeKind.ClientOperationInvocation"/> node renders
/// protocol-neutral wording describing only the call site's own compiler-proven result disposition and
/// declared fault contract — never a network call, a response received, or a thrown/observed fault —
/// and projects a concise diagram participant/message distinct from the inbound caller, deterministically
/// across repeated runs.
/// </summary>
public sealed class CoreWcfClientInvocationWordingTests
{
    private const string ContractTypeName = "CoreWcfServices.ICalculatorService";
    private const string ClientTypeName = "CoreWcfServices.CalculatorSourceClient";
    private static readonly MethodId CallerMethod = new("method:v1:CoreWcfServices.CalculatorClientCaller.CallAssigned");

    [Fact]
    public void AssignedAwaitedResultWithADeclaredFaultRendersTheFullProtocolNeutralClaim()
    {
        var graph = CreateGraph(new ScenarioNodePresentation(
            ContractTypeName: ContractTypeName,
            ClientTypeName: ClientTypeName,
            CalledMemberName: "Add",
            TargetContainingTypeName: ClientTypeName,
            TargetMemberName: "Add",
            ClientKind: ServiceClientKind.SourceClient,
            ResultClaimKind: ClientInvocationResultClaimKind.ResultAssigned,
            ResultIsAwaited: true,
            ResultBindingName: "sum",
            DeclaredResultTypeName: "System.Double",
            DeclaredFaultTypeNames: "CoreWcfServices.NegativeSquareRootFault"));

        var plan = DocumentationPlanner.Plan(graph);
        var repeated = DocumentationPlanner.Plan(graph);

        var text = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "client-operation-invocation").Text;
        Assert.Equal(
            "The action calls CalculatorSourceClient.Add through the ICalculatorService service-client boundary; "
                + "the call result is assigned to sum, awaited. The operation declares fault: CoreWcfServices.NegativeSquareRootFault.",
            text);

        // Never a network/runtime claim: no execution, response, or thrown/caught/observed fault wording.
        Assert.DoesNotContain("HTTP", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("response", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("thrown", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("caught", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("observed", text, StringComparison.OrdinalIgnoreCase);

        var participant = Assert.Single(plan.Diagram.Participants, participant => participant.Label == "CalculatorSourceClient");
        Assert.NotEqual("action", participant.Key);
        var message = Assert.Single(plan.Diagram.Messages, message => message.Label == "Add");
        Assert.Equal(participant.Key, message.Target);

        Assert.Equal(
            plan.Wording.Phrases.Select(phrase => phrase.Id.Value),
            repeated.Wording.Phrases.Select(phrase => phrase.Id.Value));
    }

    [Fact]
    public void DiscardedResultNeverClaimsAResponseAndOmitsFaultWordingWhenNoneIsDeclared()
    {
        var graph = CreateGraph(new ScenarioNodePresentation(
            ContractTypeName: ContractTypeName,
            ClientTypeName: ClientTypeName,
            CalledMemberName: "Add",
            TargetContainingTypeName: ClientTypeName,
            TargetMemberName: "Add",
            ClientKind: ServiceClientKind.SourceClient,
            ResultClaimKind: ClientInvocationResultClaimKind.Discarded,
            DeclaredResultTypeName: "System.Double"));

        var plan = DocumentationPlanner.Plan(graph);

        var text = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "client-operation-invocation").Text;
        Assert.Equal(
            "The action calls CalculatorSourceClient.Add through the ICalculatorService service-client boundary; "
                + "the result is discarded; the operation declares Double.",
            text);
        Assert.DoesNotContain("fault", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("awaited", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnclaimedResultDeclaresOnlyTheResultTypeNeverAnAssignmentOrReturnClaim()
    {
        var graph = CreateGraph(new ScenarioNodePresentation(
            ContractTypeName: ContractTypeName,
            ClientTypeName: ClientTypeName,
            CalledMemberName: "Add",
            TargetContainingTypeName: ClientTypeName,
            TargetMemberName: "Add",
            ClientKind: ServiceClientKind.GeneratedClient,
            ResultClaimKind: ClientInvocationResultClaimKind.Unclaimed,
            DeclaredResultTypeName: "System.Double"));

        var plan = DocumentationPlanner.Plan(graph);

        var text = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "client-operation-invocation").Text;
        Assert.Equal(
            "The action calls CalculatorSourceClient.Add through the ICalculatorService service-client boundary; "
                + "the call is made; result type Double is declared.",
            text);
        Assert.DoesNotContain("assigned", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("returned", text, StringComparison.OrdinalIgnoreCase);
    }

    private static ScenarioGraph CreateGraph(ScenarioNodePresentation clientPresentation)
    {
        var evidence = ImmutableArray.Create(SourceEvidence("client-operation-invocation"));
        var entry = new ScenarioNode(
            new("scenario-node:v1:client-invocation:entry"), ScenarioNodeKind.EntryPoint,
            "entry-point:v1:client-invocation", CallerMethod, null, "entry", evidence, CertaintyLevel.Exact);
        var action = new ScenarioNode(
            new("scenario-node:v1:client-invocation:action"), ScenarioNodeKind.Action,
            "client-invocation", CallerMethod, null, "configured method", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.ConfiguredMethod,
                ConfiguredContainingTypeName: "CoreWcfServices.CalculatorClientCaller",
                ConfiguredMethodName: "CallAssigned",
                ConfiguredDisplaySignature: "CoreWcfServices.CalculatorClientCaller.CallAssigned()"));
        var clientNode = new ScenarioNode(
            new("scenario-node:v1:client-invocation:client"), ScenarioNodeKind.ClientOperationInvocation,
            "client-invocation:add", CallerMethod, new OperationId("operation:v1:client-invocation:add"),
            "invokes CalculatorSourceClient.Add", evidence, CertaintyLevel.Exact,
            presentation: clientPresentation);
        var graph = new ScenarioGraph(
            new("entry-point:v1:client-invocation"), ScenarioGraphTestFactory.Profile.Id, CallerMethod,
            HttpMethodKind.Unknown, "", "CoreWcfServices.CalculatorClientCaller.CallAssigned()",
            [entry, action, clientNode],
            [
                new ScenarioEdge(new("scenario-edge:v1:client-invocation:entry"), entry.Id, action.Id,
                    ScenarioEdgeKind.Entry, "", evidence, CertaintyLevel.Exact),
                new ScenarioEdge(new("scenario-edge:v1:client-invocation:call"), action.Id, clientNode.Id,
                    ScenarioEdgeKind.Call, "outbound service-client call", evidence, CertaintyLevel.Exact),
            ],
            [], "client-invocation", ScenarioTopology.Empty,
            rootKind: ScenarioRootKind.ConfiguredMethod);
        return graph;
    }

    private static EvidenceRef SourceEvidence(string artifact)
        => new(
            new EvidenceId($"evidence:v1:{artifact}"),
            EvidenceKind.Source,
            artifact,
            new SourceRange(new DocumentId("document:v1:test"), new SourcePosition(1, 0), new SourcePosition(1, 10)),
            "test-symbol",
            null,
            CertaintyLevel.Exact);
}
