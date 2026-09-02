using System.Collections.Immutable;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Wording.Tests;

/// <summary>
/// First-observable-consumer proof for issue 54: a <see cref="ScenarioNodeKind.OutboundHttpRequest"/>
/// node plans the exact frozen boundary behavior phrase and Diagram Plan message from the typed
/// <see cref="OutboundHttpRequestKind"/> alone (never string parsing), labels the external participant
/// exactly <c>HTTP boundary</c>, and never carries a URI, header, body, or credential value nor any
/// response/status/success/exception/retry claim. HARD RED until the seven production files exist.
/// </summary>
public sealed class OutboundHttpWordingTests
{
    private const string PhraseKey = "outbound-http-request";
    private static readonly MethodId RootMethod = new("method:v1:BehaviorDocumentation.OutboundHttp.SupportedRequests.Get");

    // Credential-shaped constants from SupportedRequests.cs — none of these may reach any output.
    private static readonly string[] ForbiddenSubstrings =
    [
        "AKIA" + "IOSFODNN7EXAMPLE",
        "access_token",
        "sk_" + "live_" + "51H8xExAmPlEtOkEnValue0123456789abcdef",
        "Bearer " + "sk_" + "live_",
        "{\"ping\":true}",
        "example.test",
        "https://example.test/resource",
        "Authorization",
    ];

    [Theory]
    [InlineData(OutboundHttpRequestKind.Get, "GetAsync", "GET")]
    [InlineData(OutboundHttpRequestKind.Post, "PostAsync", "POST")]
    public void TypedRequestKindPlansExactBoundaryWording(OutboundHttpRequestKind kind, string method, string verb)
    {
        var graph = CreateGraph(kind);

        var plan = DocumentationPlanner.Plan(graph);
        var repeated = DocumentationPlanner.Plan(graph);

        var phrase = Assert.Single(plan.Wording.Phrases, p => p.Key == PhraseKey);
        Assert.Equal(
            $"The method calls HttpClient.{method} at an outbound HTTP {verb} request boundary.",
            phrase.Text);

        var message = Assert.Single(plan.Diagram.Messages, m => m.Label == $"HTTP {verb} request");
        var participant = Assert.Single(plan.Diagram.Participants, p => p.Label == "HTTP boundary");
        Assert.Equal(participant.Key, message.Target);

        // No response/runtime claims.
        foreach (var word in new[] { "response", "status", "success", "exception", "retry", "received", "completed" })
        {
            Assert.DoesNotContain(word, phrase.Text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(
            plan.Wording.Phrases.Select(p => p.Id.Value),
            repeated.Wording.Phrases.Select(p => p.Id.Value));

        // Unknown / unkinded node -> no phrase, no message.
        var unknownPlan = DocumentationPlanner.Plan(CreateGraph(OutboundHttpRequestKind.Unknown));
        Assert.DoesNotContain(unknownPlan.Wording.Phrases, p => p.Key == PhraseKey);
        Assert.DoesNotContain(unknownPlan.Diagram.Messages, m => m.Label.StartsWith("HTTP ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(OutboundHttpRequestKind.Get)]
    [InlineData(OutboundHttpRequestKind.Post)]
    public void GeneratedOutputCarriesNoRequestOrResponseValues(OutboundHttpRequestKind kind)
    {
        var graph = CreateGraph(kind);
        var plan = DocumentationPlanner.Plan(graph);

        var surfaces = new List<string>();
        surfaces.AddRange(plan.Wording.Phrases.Select(p => p.Text));
        surfaces.AddRange(plan.Diagram.Messages.Select(m => m.Label));
        surfaces.AddRange(plan.Diagram.Participants.Select(p => p.Label));
        var node = Assert.Single(graph.Nodes, n => n.Kind == ScenarioNodeKind.OutboundHttpRequest);
        surfaces.Add(node.Detail);

        foreach (var surface in surfaces)
        {
            foreach (var forbidden in ForbiddenSubstrings)
            {
                Assert.DoesNotContain(forbidden, surface, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static ScenarioGraph CreateGraph(OutboundHttpRequestKind kind)
    {
        var evidence = ImmutableArray.Create(SourceEvidence("outbound-http-request"));
        var entry = new ScenarioNode(
            new("scenario-node:v1:outbound-http:entry"), ScenarioNodeKind.EntryPoint,
            "entry-point:v1:outbound-http", RootMethod, null, "entry", evidence, CertaintyLevel.Exact);
        var action = new ScenarioNode(
            new("scenario-node:v1:outbound-http:action"), ScenarioNodeKind.Action,
            "outbound-http", RootMethod, null, "configured method", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.ConfiguredMethod,
                ConfiguredContainingTypeName: "BehaviorDocumentation.OutboundHttp.SupportedRequests",
                ConfiguredMethodName: "Get",
                ConfiguredDisplaySignature: "BehaviorDocumentation.OutboundHttp.SupportedRequests.Get()"));
        var httpNode = new ScenarioNode(
            new("scenario-node:v1:outbound-http:request"), ScenarioNodeKind.OutboundHttpRequest,
            "outbound-http:request", RootMethod, new OperationId("operation:v1:outbound-http:request"),
            "outbound HTTP request", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(OutboundHttpRequestKind: kind));
        var graph = new ScenarioGraph(
            new("entry-point:v1:outbound-http"), ScenarioGraphTestFactory.Profile.Id, RootMethod,
            HttpMethodKind.Unknown, "", "BehaviorDocumentation.OutboundHttp.SupportedRequests.Get()",
            [entry, action, httpNode],
            [
                new ScenarioEdge(new("scenario-edge:v1:outbound-http:entry"), entry.Id, action.Id,
                    ScenarioEdgeKind.Entry, "", evidence, CertaintyLevel.Exact),
                new ScenarioEdge(new("scenario-edge:v1:outbound-http:call"), action.Id, httpNode.Id,
                    ScenarioEdgeKind.Call, "outbound HTTP request", evidence, CertaintyLevel.Exact),
            ],
            [], "outbound-http", ScenarioTopology.Empty,
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
