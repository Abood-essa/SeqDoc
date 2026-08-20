using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using SeqDoc.FrameworkModels.EntityFramework;
using SeqDoc.FrameworkModels.FusionCache;
using SeqDoc.Rendering.Markdown;
using Xunit;
using SeqDoc.Testing;

namespace SeqDoc.AcceptanceTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BehaviorDocumentationLevel2Group
{
    public const string Name = "Translation alpha Level 2";
}

/// <summary>
/// Translation-alpha Level 2 acceptance for the selected real application flow
/// <c>CustomerManagement.Api.Controllers.CustomerController.GetCustomerById(int id)</c>. The suite owns only the real-
/// application and milestone assertions: the supplied external checkout is only read and copied and is never
/// cleaned, restored, or
/// analyzed; every analysis runs from a temporary git-free source copy; intended operation
/// discovery; the accepted contract exact SQL/JSON composition for the configuration-exclusive
/// SqlCustomerService/JsonCustomerService registrations with canonical per-arm member nodes; the
/// accepted contract exact FusionCache <c>GetOrSetAsync</c> cache-miss callback region joining the SQL arm's EF
/// query and the configuration <c>alt</c> with the nested <c>On cache miss</c> <c>opt</c> in the
/// planned diagram; exact RootMethod resolution through the Program Index; concise collision-safe
/// participants (<c>API client</c>, <c>CustomerController</c>, <c>Customer service</c>,
/// <c>AppDbContext</c>) with evidence and certainty on every graph and wording element; an honest
/// FusionCache/delegate boundary that never claims a cache hit or distributed-cache behavior; and
/// byte-identical Markdown/Mermaid/index/manifest/Program-Index-fingerprint output across repeated
/// analysis and two independent relocated temporary checkouts with no fingerprint or hash
/// normalization. All activated output remains under test-owned temporary directories. Detailed compiler and presentation partitions stay below this boundary and are already
/// protected by the accepted contract, Get/Presentation, and accepted contract framework/scenario/wording suites.
/// </summary>
[Collection(BehaviorDocumentationLevel2Group.Name)]
public sealed class BehaviorDocumentationLevel2Tests
{
    private static string ExternalRepositoryRoot => Path.Combine(
        ExternalCorpusResolver.Current.RequireGroup(ExternalCorpusGroup.Provided).Root, "testRepo");
    private const string ExternalProjectRelativePath = "CustomerManagement.Api/CustomerManagement.Api.csproj";
    private const string ExpectedOperationKey = "GET api/Customer/{id}";

    /// <summary>
    /// accepted contract F2 ownership inventory: the exact repository-controlled appsettings files the temporary
    /// checkout copies for the approved CustomerManagement.Api project. Only these paths may contribute
    /// checked-in observations; everything else stays ambient and fails closed.
    /// </summary>
    private static readonly ImmutableArray<string> CustomerManagementOwnedConfigurationFiles =
    [
        "CustomerManagement.Api/appsettings.json",
        "CustomerManagement.Api/appsettings.Development.json",
    ];

    /// <summary>
    /// Concise collision-safe participant labels the accepted accepted contract Level 2 graph must show: the
    /// client, the controller, the composition contract role (never an implementation class name),
    /// and the EF DbContext short name, with no dots, full namespaces, or implementation class
    /// names. Kept as a static readonly array to satisfy CA1861.
    /// </summary>
    private static readonly string[] ExpectedConciseParticipantLabels =
    [
        "API client",
        "AppDbContext",
        "Customer service",
        "CustomerController.GetCustomerById",
    ];

    /// <summary>
    /// Semantic/presentation honesty for the selected Level 2 flow under the accepted contract/accepted contract boundary.
    /// The external Program.cs registers <c>ICustomerService</c> twice behind a configuration toggle
    /// (<c>SqlCustomerService</c> and <c>JsonCustomerService</c>). One exact same-condition
    /// alternative group accounts for the complete binding set, so the generic pipeline resolves the
    /// pair through <c>ScenarioGraph.Composition</c> with SQL true and JSON false arms and exactly
    /// one resolved method per arm; SC001 is absent for this proven pair and checked-in JSON never
    /// selects an arm. accepted contract materializes each arm's exact member nodes (the SQL arm carries its
    /// service node and its single EF query; the JSON arm carries only its service node) and joins
    /// the query into exactly one zero-or-one conditional cache-miss callback region, so the diagram
    /// renders one configuration <c>alt</c> with a nested <c>On cache miss</c> <c>opt</c> and never
    /// presents the query as unconditional SQL work or claims a cache hit or distributed-cache
    /// behavior. The graph must prove the intended GET operation and the pinned root method, retain
    /// evidence and non-Unknown certainty on every node, edge, and wording phrase, keep concise
    /// collision-safe <c>API client</c>/<c>CustomerController</c>/<c>Customer service</c>/
    /// <c>AppDbContext</c> participants without dots, fully qualified names, or implementation class
    /// names, and stay free of TicketReservation/application-name coupling. The analysis itself runs on a temporary git-free source copy
    /// so the supplied external checkout is never modified.
    /// </summary>
    [Fact]
    public async Task Level2CustomerGetFlowFailsClosedOnDualConfigurationRegistrationsWithConciseEvidenceBackedPresentation()
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"seqdoc-ta5-level2-semantics-{Guid.NewGuid():N}");
        try
        {
            var copy = await PrepareTemporaryCopyAsync(temporary);
            var profile = CompilationProfile.Create(ExternalProjectRelativePath, "Release", "net10.0");
            var bundle = await BuildAsync(copy.CheckoutRoot, copy.TargetPath, profile);

            // Risk 1: the generic pipeline discovers exactly the intended GET operation and never
            // couples to TicketReservation vocabulary or to the external or temporary checkout path
            // in its canonical projections.
            var get = Assert.Single(
                bundle.Graphs.Graphs,
                graph => graph.OperationKey == ExpectedOperationKey);
            Assert.DoesNotContain("TicketReservation", bundle.Graphs.DebugProjection, StringComparison.Ordinal);
            Assert.DoesNotContain("ReservationService", bundle.Graphs.DebugProjection, StringComparison.Ordinal);
            Assert.DoesNotContain(ExternalRepositoryRoot, bundle.Graphs.DebugProjection, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(copy.CheckoutRoot, bundle.Graphs.DebugProjection, StringComparison.OrdinalIgnoreCase);

            // Risk 5: the selected graph's RootMethod resolves through the Program Index to exactly
            // the pinned controller action CustomerController.GetCustomerById with its single
            // System.Int32 value parameter.
            AssertRootMethodResolvesThroughProgramIndex(get, bundle.Extraction.ProgramIndex);

            // Risk 1 (amended accepted contract boundary): the cause is exactly the two configuration-exclusive
            // ICustomerService registrations (SqlCustomerService and JsonCustomerService). Proving both
            // registrations exist and that one exact same-condition alternative group accounts for the
            // complete binding set lets the proven pair advance beyond SC001; neither branch is ever
            // flattened into visible behavior.
            var customerServiceRegistrations = bundle.Extraction.DependencyInjectionFacts.Registrations
                .Where(registration => registration.ServiceType == "CustomerManagement.Api.Services.ICustomerService")
                .ToArray();
            Assert.Equal(2, customerServiceRegistrations.Length);
            Assert.Equal(
                [
                    "CustomerManagement.Api.Services.JsonCustomerService",
                    "CustomerManagement.Api.Services.SqlCustomerService",
                ],
                customerServiceRegistrations
                    .Select(registration => registration.ImplementationType)
                    .Order(StringComparer.Ordinal));
            Assert.All(
                customerServiceRegistrations,
                registration => Assert.Equal(DependencyInjectionLifetime.Scoped, registration.Lifetime));

            // accepted contract exact composition: the pinned Program.cs if/else (FeatureToggles:UseSqlDatabase)
            // registers exactly SqlCustomerService on the true arm and JsonCustomerService on the
            // false arm. The graph carries one configuration decision and two independently resolved
            // service arms, and SC001 is absent for this exact proven pair.
            Assert.DoesNotContain(get.Diagnostics, candidate => candidate.Code == "SC001");
            var composition = Assert.IsType<ScenarioServiceComposition>(get.Composition);
            Assert.Equal("CustomerManagement.Api.Services.ICustomerService", composition.ServiceType);
            Assert.Equal("FeatureToggles:UseSqlDatabase", composition.Decision.Key);
            Assert.True(composition.TrueArm.IsTrue);
            Assert.Equal("CustomerManagement.Api.Services.SqlCustomerService", composition.TrueArm.ImplementationType);
            Assert.False(composition.FalseArm.IsTrue);
            Assert.Equal("CustomerManagement.Api.Services.JsonCustomerService", composition.FalseArm.ImplementationType);

            // Exact per-arm method identities resolve through ProgramIndex metadata+member: each arm's
            // ResolvedMethod must equal the exact candidate MethodId of the implementation member,
            // never a reconstructed display string in a MethodId.
            var sqlServiceType = Assert.Single(
                bundle.Extraction.ProgramIndex.Types,
                type => type.MetadataName == "CustomerManagement.Api.Services.SqlCustomerService");
            var jsonServiceType = Assert.Single(
                bundle.Extraction.ProgramIndex.Types,
                type => type.MetadataName == "CustomerManagement.Api.Services.JsonCustomerService");
            var expectedSqlMethod = Assert.Single(
                bundle.Extraction.ProgramIndex.Methods,
                method => method.ContainingType == sqlServiceType.Id && method.Name == "GetCustomerByIdAsync");
            var expectedJsonMethod = Assert.Single(
                bundle.Extraction.ProgramIndex.Methods,
                method => method.ContainingType == jsonServiceType.Id && method.Name == "GetCustomerByIdAsync");
            Assert.Equal(expectedSqlMethod.Id, composition.TrueArm.ResolvedMethod);
            Assert.Equal(expectedJsonMethod.Id, composition.FalseArm.ResolvedMethod);
            Assert.NotEqual(composition.TrueArm.ResolvedMethod, composition.FalseArm.ResolvedMethod);

            // The decision retains the group and configuration evidence with the weakest certainty:
            // the Conservative checked-in observation governs and never promotes the decision.
            Assert.NotEmpty(composition.Decision.Evidence);
            Assert.NotEqual(CertaintyLevel.Unknown, composition.Decision.Certainty);
            Assert.True(composition.Decision.Certainty >= composition.Decision.Evidence.Max(item => item.Certainty));
            Assert.Equal(CertaintyLevel.Conservative, composition.Decision.Certainty);

            // accepted contract facts-only boundary: the pinned CustomerManagement flow admits the exact
            // FeatureToggles:UseSqlDatabase boolean read and its checked-in true observation. The fact
            // set is additive and memory-only; the composition and the sparse API client/
            // CustomerController graph stay deterministic and the DI branch association is exact.
            var configurationFacts = bundle.Extraction.ConfigurationSemanticFacts;
            var pinnedRead = Assert.Single(
                configurationFacts.Reads,
                fact => fact.Key == "FeatureToggles:UseSqlDatabase");
            Assert.Equal(CertaintyLevel.Exact, pinnedRead.Certainty);
            Assert.Null(pinnedRead.DefaultValue);
            Assert.NotEmpty(pinnedRead.Evidence);

            var checkedInTrue = Assert.Single(
                configurationFacts.CheckedInValues,
                fact => fact.Key == "FeatureToggles:UseSqlDatabase" && fact.Value);
            Assert.Equal(CertaintyLevel.Conservative, checkedInTrue.Certainty);
            Assert.True(checkedInTrue.MayBeOverridden);

            // The checked-in true observation never selects SQL: the default analysis profile carries
            // no profile-known value, so both arms remain possible and no selection metadata exists.
            Assert.Null(composition.ProfileSelection);
            Assert.Empty(configurationFacts.ProfileKnownValues);

            // accepted contract materialization: the complete composition arms are visible in the flat graph.
            // Exactly two service nodes (one per arm) match the resolved SQL/JSON methods, the
            // arms' canonical member nodes are non-empty and disjoint, and the single EF query
            // belongs only to the SQL arm. The graph still withholds any HTTP outcome claim.
            var serviceNodes = get.Nodes.Where(node => node.Kind == ScenarioNodeKind.ServiceCall).ToArray();
            Assert.Equal(2, serviceNodes.Length);
            var sqlServiceNode = Assert.Single(serviceNodes, node => node.Method == expectedSqlMethod.Id);
            var jsonServiceNode = Assert.Single(serviceNodes, node => node.Method == expectedJsonMethod.Id);
            Assert.NotEmpty(composition.TrueArm.MemberNodes);
            Assert.NotEmpty(composition.FalseArm.MemberNodes);
            Assert.Empty(composition.TrueArm.MemberNodes.Intersect(composition.FalseArm.MemberNodes));
            Assert.Contains(sqlServiceNode.Id, composition.TrueArm.MemberNodes);
            Assert.Contains(jsonServiceNode.Id, composition.FalseArm.MemberNodes);

            var queryNodes = get.Nodes.Where(node => node.Kind == ScenarioNodeKind.EntityQuery).ToArray();
            Assert.True(
                queryNodes.Length == 1,
                BuildQueryGapDiagnostic(bundle, expectedSqlMethod.Id));
            var queryNode = Assert.Single(queryNodes);
            var queryEdge = Assert.Single(get.Edges, edge => edge.Kind == ScenarioEdgeKind.Query);
            Assert.Equal(sqlServiceNode.Id, queryEdge.Source);
            Assert.Equal(queryNode.Id, queryEdge.Target);
            Assert.Contains(queryNode.Id, composition.TrueArm.MemberNodes);
            Assert.DoesNotContain(queryNode.Id, composition.FalseArm.MemberNodes);
            Assert.DoesNotContain(get.Nodes, node => node.Kind == ScenarioNodeKind.Outcome);

            // accepted contract cache-miss join: exactly one framework-conditional callback region carries the
            // zero-or-one conditional CacheMiss semantics with no operation trigger condition and
            // exactly the EF query node as its member (asserted element-wise so a duplicate member
            // would fail Assert.Single instead of comparing collection-to-collection).
            var callbackRegion = Assert.Single(get.CallbackRegions);
            Assert.Equal(CallbackCardinality.ZeroOrOne, callbackRegion.Cardinality);
            Assert.Equal(CallbackTriggerKind.Conditional, callbackRegion.Trigger);
            Assert.Null(callbackRegion.TriggerCondition);
            Assert.Equal(FrameworkCallbackConditionKind.CacheMiss, callbackRegion.FrameworkCondition);
            Assert.Equal(queryNode.Id, Assert.Single(callbackRegion.MemberNodes));

            // Risk 2: every node and edge retains non-empty evidence and explicit non-Unknown certainty.
            foreach (var node in get.Nodes)
            {
                Assert.NotEmpty(node.Evidence);
                Assert.NotEqual(CertaintyLevel.Unknown, node.Certainty);
            }

            foreach (var edge in get.Edges)
            {
                Assert.NotEmpty(edge.Evidence);
                Assert.NotEqual(CertaintyLevel.Unknown, edge.Certainty);
            }

            // Risk 2: concise collision-safe participant labels are exactly the four accepted contract labels
            // (client, controller, composition contract role, and DbContext short name), with no
            // dots, fully qualified names, or implementation class names.
            var plan = DocumentationPlanner.Plan(get);
            string[] participantLabels = plan.Diagram.Participants
                .Select(participant => participant.Label)
                .ToArray();
            Assert.Equal(
                ExpectedConciseParticipantLabels,
                participantLabels.Order(StringComparer.Ordinal));
            foreach (string label in participantLabels)
            {
                Assert.True(label.Count(character => character == '.') <= 1, label);
                Assert.DoesNotContain("CustomerManagement", label, StringComparison.Ordinal);
            }

            // accepted contract diagram: the plan owns one configuration Alt ("Use SQL database") after the
            // entry request, with the SQL arm carrying the nested "On cache miss" Opt around the
            // query and the JSON arm carrying no Opt/query. No legacy flat branches exist, and the
            // ordered sequence tree references every planned message exactly once.
            Assert.Empty(plan.Diagram.Branches);
            var sequenceElements = plan.Diagram.Sequence.Elements;
            Assert.Equal(2, sequenceElements.Length);
            Assert.True(sequenceElements[0].IsMessageRef);
            Assert.True(sequenceElements[1].IsFragment);
            var configurationAlt = sequenceElements[1].NestedFragment!;
            Assert.Equal(DiagramFragmentKind.Alt, configurationAlt.Kind);
            Assert.Equal("Use SQL database", configurationAlt.Label);
            Assert.Equal(2, configurationAlt.Arms.Length);
            Assert.False(configurationAlt.Arms[0].IsElse);
            Assert.True(configurationAlt.Arms[1].IsElse);
            Assert.Contains("SQL", configurationAlt.Arms[0].Label, StringComparison.Ordinal);
            Assert.Contains("JSON", configurationAlt.Arms[1].Label, StringComparison.Ordinal);
            Assert.NotEmpty(configurationAlt.Arms[0].MessageRefs);
            Assert.NotEmpty(configurationAlt.Arms[1].MessageRefs);

            string queryRefValue = "diagram-element:v1:message:" + queryEdge.Id.Value;
            var sqlOpt = Assert.Single(configurationAlt.Arms[0].Fragments);
            Assert.Equal(DiagramFragmentKind.Opt, sqlOpt.Kind);
            Assert.Equal("On cache miss", sqlOpt.Label);
            var queryRef = Assert.Single(sqlOpt.MessageRefs, reference => reference.Value == queryRefValue);
            Assert.Empty(configurationAlt.Arms[1].Fragments);
            Assert.DoesNotContain(
                configurationAlt.Arms[1].MessageRefs,
                reference => reference.Value == queryRefValue);

            var entryRef = plan.Diagram.Messages.Single(message => message.Label == ExpectedOperationKey).Id;
            Assert.Equal(entryRef, sequenceElements[0].MessageRefId);
            var sequenceRefs = CollectSequenceRefs(plan.Diagram);
            Assert.Equal(sequenceRefs.Length, sequenceRefs.Distinct().Count());
            Assert.Equal(plan.Diagram.Messages.Length, sequenceRefs.Length);

            string markdown = MarkdownRenderer.RenderDocument(plan.Wording, plan.Diagram);
            Assert.DoesNotContain("CustomerManagement.Api.", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("HTTP 200", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", markdown, StringComparison.Ordinal);
            Assert.Contains("SQL", markdown, StringComparison.Ordinal);
            Assert.Contains("JSON", markdown, StringComparison.Ordinal);
            Assert.Contains("Use SQL database", markdown, StringComparison.Ordinal);
            Assert.Contains("On cache miss", markdown, StringComparison.Ordinal);
            string queryLabel = plan.Diagram.Messages.Single(message => message.Id == queryRef).Label;
            Assert.Equal(1, markdown.Split(queryLabel).Length - 1);
            Assert.True(
                markdown.IndexOf(queryLabel, StringComparison.Ordinal)
                > markdown.IndexOf("On cache miss", StringComparison.Ordinal));

            // The checked-in true observation never selects SQL and no runtime/deployment claim is
            // invented: no default, selection, universal, distributed, or cache-hit wording appears.
            Assert.DoesNotContain("default", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("selected", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("universal", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("distributed", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cache hit", markdown, StringComparison.OrdinalIgnoreCase);

            // Risk 2/3 (amended accepted contract boundary): with the exact proven pair resolved through
            // composition, no SC001 technical fallback remains and neither alternative is flattened
            // into a confident service claim in wording.
            Assert.DoesNotContain(
                plan.Wording.Phrases,
                phrase => phrase.Key == "fallback:SC001");

            // Risk 2/3: every wording phrase is evidence-backed with explicit certainty, and no
            // generic cache wording leaks into the behavior statements: the specific framework
            // "On cache miss" fragment lives only in the diagram, never in a wording phrase.
            foreach (var phrase in plan.Wording.Phrases)
            {
                Assert.NotEmpty(phrase.Evidence);
                Assert.NotEqual(CertaintyLevel.Unknown, phrase.Certainty);
                Assert.DoesNotContain("cache", phrase.Text, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    /// <summary>
    /// Deterministic repeated/relocated verification lane. The supplied external checkout is never
    /// cleaned, restored, or analyzed; instead the approved project/source is copied into two
    /// independent temporary git-free roots, and both are restored with the identical restore
    /// command. The first root is analyzed twice and the second once, and the Program Index
    /// fingerprint, canonical debug projection, and every generated Markdown/Mermaid/index/manifest
    /// byte must be exactly equal across all three analyses — with no fingerprint or hash
    /// normalization, because both roots are intentionally git-free and restore from identical
    /// inputs, so any difference is checkout-path nondeterminism rather than an accepted input
    /// difference. No checkout path (external or temporary) may leak into generated output. When
    /// all activated output is written beneath test-owned temporary directories.
    /// </summary>
    [Fact]
    public async Task Level2OutputIsDeterministicAcrossRepeatedAnalysisAndRelocatedCheckout()
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"seqdoc-ta5-level2-{Guid.NewGuid():N}");
        try
        {
            string rootA = Path.Combine(temporary, "root-a");
            string rootB = Path.Combine(temporary, "root-b");
            var copyA = await PrepareTemporaryCopyAsync(rootA);
            var copyB = await PrepareTemporaryCopyAsync(rootB);
            var profile = CompilationProfile.Create(ExternalProjectRelativePath, "Release", "net10.0");

            // Repeated analysis of the first root and one analysis of the second independent
            // git-free root must be byte-identical: the Program Index fingerprint, the canonical
            // debug projection, and every generated file (operation Markdown, Mermaid, and index)
            // are compared exactly with no normalization.
            var firstA = await BuildSetAsync(copyA.CheckoutRoot, copyA.TargetPath, profile);
            var secondA = await BuildSetAsync(copyA.CheckoutRoot, copyA.TargetPath, profile);
            var firstB = await BuildSetAsync(copyB.CheckoutRoot, copyB.TargetPath, profile);

            Assert.Equal(firstA.Graphs.ProgramIndexFingerprint, secondA.Graphs.ProgramIndexFingerprint);
            Assert.Equal(firstA.Graphs.ProgramIndexFingerprint, firstB.Graphs.ProgramIndexFingerprint);
            Assert.Equal(firstA.Graphs.DebugProjection, secondA.Graphs.DebugProjection);
            Assert.Equal(firstA.Graphs.DebugProjection, firstB.Graphs.DebugProjection);
            AssertSameFiles(firstA.Built, secondA.Built);
            AssertSameFiles(firstA.Built, firstB.Built);

            // The accepted contract composition identity is part of the deterministic projection: it is stable
            // across repeated analysis and two independent relocated roots.
            Assert.Equal(CompositionIdOf(firstA), CompositionIdOf(secondA));
            Assert.Equal(CompositionIdOf(firstA), CompositionIdOf(firstB));

            // The accepted contract cache-miss callback region identity and canonical member-node set are also
            // part of the deterministic projection: the same region joins the same EF query node
            // across repeated analysis and two independent relocated roots.
            Assert.Equal(CallbackRegionIdOf(firstA), CallbackRegionIdOf(secondA));
            Assert.Equal(CallbackRegionIdOf(firstA), CallbackRegionIdOf(firstB));

            // Activated output, including the ownership manifest, is byte-identical across the two
            // independent roots.
            string outputA = Path.Combine(temporary, "output-a");
            string outputB = Path.Combine(temporary, "output-b");
            ActivateAndAssert(outputA, firstA.Built);
            ActivateAndAssert(outputB, firstB.Built);
            AssertSameActivatedOutput(outputA, outputB, firstA.Built.Files);

            // The generated output must be path-free: neither the external checkout nor either
            // temporary checkout root may leak into Markdown, and relocated analysis keeps its
            // canonical path-free debug projection.
            foreach (var file in firstA.Built.Files.Where(file =>
                         file.RelativePath.EndsWith(".md", StringComparison.Ordinal)))
            {
                string content = Encoding.UTF8.GetString(file.Content);
                Assert.DoesNotContain(ExternalRepositoryRoot, content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(copyA.CheckoutRoot, content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(copyB.CheckoutRoot, content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("\r", content, StringComparison.Ordinal);
                AssertSingleH1AndBalancedFences(file.RelativePath, content);
            }

            Assert.DoesNotContain(copyA.CheckoutRoot, firstA.Graphs.DebugProjection, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(copyB.CheckoutRoot, firstB.Graphs.DebugProjection, StringComparison.OrdinalIgnoreCase);

        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private sealed record Level2Bundle(
        ScenarioGraphSet Graphs,
        ProfileAnalysisExtraction Extraction,
        BehaviorSnapshot Behavior,
        FrameworkAnalysisResult Framework);

    private sealed record Level2Output(ScenarioGraphSet Graphs, DocumentationSetBuildResult Built);

    /// <summary>
    /// Extracts the exact accepted contract composition identity of the selected Level 2 graph so the relocation
    /// lane can prove the composition is deterministic across repeated and relocated analysis.
    /// </summary>
    private static string CompositionIdOf(Level2Output output)
    {
        var graph = Assert.Single(
            output.Graphs.Graphs,
            candidate => candidate.OperationKey == ExpectedOperationKey);
        var composition = Assert.IsType<ScenarioServiceComposition>(graph.Composition);
        return composition.Id.Value;
    }

    /// <summary>
    /// Extracts the exact accepted contract cache-miss callback region identity and canonical member-node set of
    /// the selected Level 2 graph so the relocation lane can prove the callback join is
    /// deterministic across repeated and relocated analysis.
    /// </summary>
    private static string CallbackRegionIdOf(Level2Output output)
    {
        var graph = Assert.Single(
            output.Graphs.Graphs,
            candidate => candidate.OperationKey == ExpectedOperationKey);
        var region = Assert.Single(graph.CallbackRegions);
        return region.Id.Value + "|" + string.Join(
            ",",
            region.MemberNodes.Select(node => node.Value).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Builds a deterministic multiline test-only diagnostic when the exact EF query node is
    /// missing or duplicated in the selected Level 2 graph. This is instrumentation for a failing
    /// <see cref="Assert.Single"/> and never changes the asserted semantics; it reports the SQL
    /// method's operations (identity, supplied parameter ordinals, target identity including
    /// assembly version, generic arity, return type, and indexed parameter refkind/type sequence,
    /// and query chain shape), the SQL caller's
    /// callback boundaries (outer invocation, ordinal, and member operations), the framework facts
    /// (runtime type, method, and operation), and the framework diagnostics (codes and technical
    /// causes) so a query gap can be understood from compiler evidence instead of guessed. Every
    /// collection is ordered canonically so the message is stable across repeated failures.
    /// </summary>
    private static string BuildQueryGapDiagnostic(Level2Bundle bundle, MethodId expectedSqlMethodId)
    {
        var graph = bundle.Graphs.Graphs.FirstOrDefault(
            candidate => candidate.OperationKey == ExpectedOperationKey);
        var lines = new List<string>
        {
            $"Expected exactly one EntityQuery node for SQL method '{expectedSqlMethodId.Value}'.",
            graph is null
                ? "Selected Level 2 graph is missing."
                : "EntityQuery nodes found in selected graph: "
                    + string.Join(", ", graph.Nodes
                        .Where(node => node.Kind == ScenarioNodeKind.EntityQuery)
                        .Select(node => node.Id.Value)
                        .Order(StringComparer.Ordinal)),
        };

        lines.Add("SQL method operations:");
        var operations = bundle.Extraction.Operations
            .Where(operation => operation.Method == expectedSqlMethodId)
            .OrderBy(operation => operation.Id.Value, StringComparer.Ordinal)
            .ToArray();
        if (operations.Length == 0)
        {
            lines.Add("  (none)");
        }

        foreach (var operation in operations)
        {
            string ordinals = operation.SuppliedParameterOrdinals.IsDefault
                ? "(default)"
                : string.Join(",", operation.SuppliedParameterOrdinals);
            string target = operation.TargetIdentity is null
                ? "null"
                : $"{operation.TargetIdentity.MethodMetadataName} in "
                    + $"{operation.TargetIdentity.ContainingMetadataType} "
                    + $"(assembly {operation.TargetIdentity.AssemblyIdentity}; "
                    + $"version {operation.TargetIdentity.AssemblyVersion ?? "null"}; "
                    + $"arity {operation.TargetIdentity.GenericArity}; "
                    + $"return {operation.TargetIdentity.ReturnType ?? "null"}; "
                    + $"parameters ["
                    + (operation.TargetIdentity.Parameters.IsDefault
                        ? "(default)"
                        : string.Join(", ", operation.TargetIdentity.Parameters.Select((parameter, index) =>
                            $"{index}:{parameter.RefKind} {parameter.FullyQualifiedType}")))
                    + "])";
            string chain = operation.QueryChain is null
                ? "null"
                : $"ReceiverType={operation.QueryChain.ReceiverType}; "
                    + $"ContainingType={operation.QueryChain.ContainingType}; "
                    + $"MemberName={operation.QueryChain.MemberName}; "
                    + $"EntityType={operation.QueryChain.EntityType}; "
                    + $"Steps=[{string.Join(", ", operation.QueryChain.Steps.Select(step =>
                        $"operation:{step.Operation.Value}; "
                        + $"target:{step.TargetIdentity.MethodMetadataName}; "
                        + $"navigation:{step.NavigationMemberIdentity ?? "null"}"))}]";
            lines.Add(
                $"  operation:{operation.Id.Value}; kind:{operation.Kind}; suppliedOrdinals:[{ordinals}]; "
                + $"target:{target}; queryChain:{chain}");
        }

        lines.Add("SQL callback boundaries:");
        var boundaries = bundle.Extraction.CallbackBoundaryFacts.Boundaries
            .Where(boundary => boundary.CallerMethod == expectedSqlMethodId)
            .OrderBy(boundary => boundary.Id.Value, StringComparer.Ordinal)
            .ToArray();
        if (boundaries.Length == 0)
        {
            lines.Add("  (none)");
        }

        foreach (var boundary in boundaries)
        {
            lines.Add(
                $"  boundary:{boundary.Id.Value}; outer:{boundary.OuterInvocationOperation.Value}; "
                + $"ordinal:{boundary.ParameterOrdinal}; targetKind:{boundary.TargetKind}; "
                + $"members:[{string.Join(", ", boundary.MemberOperations)}]");
        }

        lines.Add("Framework facts:");
        if (bundle.Framework.Facts.IsDefaultOrEmpty)
        {
            lines.Add("  (none)");
        }

        foreach (var fact in bundle.Framework.Facts.OrderBy(fact => fact.Id.Value, StringComparer.Ordinal))
        {
            string detail = fact switch
            {
                FusionCacheGetOrSetFact cacheFact =>
                    $"method:{cacheFact.Method.Value}; operation:{cacheFact.Operation.Value}; "
                    + $"factoryOrdinal:{cacheFact.FactoryParameterOrdinal}; "
                    + $"contractVersion:{cacheFact.ContractVersion}; "
                    + $"cardinality:{cacheFact.Cardinality}; trigger:{cacheFact.Trigger}; "
                    + $"condition:{cacheFact.Condition}",
                _ => $"id:{fact.Id.Value}; certainty:{fact.Certainty}",
            };
            lines.Add($"  runtimeType:{fact.GetType().Name}; {detail}");
        }

        lines.Add("Framework diagnostics:");
        if (bundle.Framework.Diagnostics.IsDefaultOrEmpty)
        {
            lines.Add("  (none)");
        }

        foreach (var diagnostic in bundle.Framework.Diagnostics
                     .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal))
        {
            lines.Add($"  code:{diagnostic.Code}; cause:{diagnostic.TechnicalCause}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Recursively collects every diagram message reference in the ordered sequence tree (sequence
    /// elements, fragment message refs, and alt-arm message refs) so the acceptance lane can prove
    /// the plan references every planned message exactly once with no duplication or omission.
    /// </summary>
    private static ImmutableArray<DiagramPlanElementId> CollectSequenceRefs(DiagramPlan plan)
    {
        var refs = new List<DiagramPlanElementId>();
        void WalkElements(ImmutableArray<DiagramSequenceElement> elements)
        {
            foreach (var element in elements)
            {
                if (element.IsMessageRef)
                {
                    refs.Add(element.MessageRefId!.Value);
                }
                else
                {
                    WalkFragment(element.NestedFragment!);
                }
            }
        }

        void WalkFragment(DiagramFragment fragment)
        {
            refs.AddRange(fragment.MessageRefs);
            foreach (var arm in fragment.Arms)
            {
                refs.AddRange(arm.MessageRefs);
                foreach (var nested in arm.Fragments)
                {
                    WalkFragment(nested);
                }
            }

            foreach (var nested in fragment.Fragments)
            {
                WalkFragment(nested);
            }
        }

        WalkElements(plan.Sequence.Elements);
        return refs.ToImmutableArray();
    }

    private sealed record TemporaryProjectCopy(string CheckoutRoot, string TargetPath);

    private static async Task<Level2Output> BuildSetAsync(string root, string target, CompilationProfile profile)
    {
        var bundle = await BuildAsync(root, target, profile);
        var selected = Assert.Single(
            bundle.Graphs.Graphs,
            graph => graph.OperationKey == ExpectedOperationKey);
        var plan = DocumentationPlanner.Plan(selected);
        string fileName = DocumentationFileNaming.EntryKey(selected.EntryPoint, selected.OperationKey);
        var built = DocumentationSetBuilder.Build(
            bundle.Graphs.Profile.Id.Value,
            bundle.Graphs.ProgramIndexFingerprint,
            [new DocumentSetEntry(fileName, plan.Wording, plan.Diagram)]);
        Assert.True(built.Succeeded, string.Join("; ", built.Errors));

        // The documentation set is scoped to the exact selected operation graph: exactly one
        // operation Markdown file, its Mermaid sibling, and the profile index. Whole-analysis
        // semantic claims stay on bundle.Graphs (program-index fingerprint, debug projection, and
        // the operation diagnostics the other assertions consume).
        Assert.Equal(
            1,
            built.Files.Count(file =>
                file.RelativePath.EndsWith(".md", StringComparison.Ordinal)
                && file.RelativePath != "index.md"));
        Assert.Equal(
            1,
            built.Files.Count(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)));
        return new Level2Output(bundle.Graphs, built);
    }

    private static async Task<Level2Bundle> BuildAsync(string root, string target, CompilationProfile profile)
    {
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(
                root,
                target,
                profile,
                RepositoryOwnedConfigurationFiles: CustomerManagementOwnedConfigurationFiles),
            CancellationToken.None);
        Assert.True(
            extraction.IsSuccess,
            string.Join(Environment.NewLine, extraction.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var analysis = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(
            analysis.IsSuccess,
            string.Join(Environment.NewLine, analysis.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var host = new FrameworkModelHost(
        [
            new AspNetCoreControllerModel(),
            new EntityFrameworkQueryModel(),
            new FusionCacheGetOrSetModel(),
        ]);
        var framework = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(
                    profile,
                    extraction.Value.ProgramIndex,
                    extraction.Value.CallbackBoundaryFacts),
                extraction.Value.Operations,
                extraction.Value.Symbols),
            CancellationToken.None);

        var graphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile,
            extraction.Value.ProgramIndex,
            analysis.Value!,
            framework,
            extraction.Value.SemanticFacts,
            extraction.Value.DependencyInjectionFacts,
            extraction.Value.StructuralResultFacts,
            extraction.Value.NonGetSemanticFacts,
            extraction.Value.ConditionalDependencyInjectionFacts,
            extraction.Value.ConfigurationSemanticFacts,
            extraction.Value.CallbackBoundaryFacts));
        return new Level2Bundle(graphs, extraction.Value, analysis.Value!, framework);
    }

    /// <summary>
    /// Creates one temporary git-free source copy of the approved external project and restores it
    /// with the identical restore command used for every other temporary copy. Only
    /// repository-controlled project and source files are copied; the external checkout itself is
    /// never cleaned, restored, or analyzed.
    /// </summary>
    private static async Task<TemporaryProjectCopy> PrepareTemporaryCopyAsync(string scratchRoot)
    {
        string checkoutRoot = Path.Combine(scratchRoot, "checkout");
        string projectDirectory = Path.Combine(checkoutRoot, "CustomerManagement.Api");
        CopyRepositoryControlledSource(
            Path.Combine(ExternalRepositoryRoot, "CustomerManagement.Api"),
            projectDirectory);
        await RestoreAsync(projectDirectory);
        return new TemporaryProjectCopy(checkoutRoot, Path.Combine(projectDirectory, "CustomerManagement.Api.csproj"));
    }

    /// <summary>
    /// Copies only repository-controlled files required to build and analyze one project: the project
    /// file, every *.cs source beneath it, and the repository-controlled appsettings*.json files.
    /// .git, bin, obj, user-secrets, local settings that are not repository-controlled (*.user,
    /// launchSettings.json), databases, logs, and generated build output are never copied, so the
    /// destination is a git-free source copy. The appsettings files are copied so the accepted contract checked-in
    /// configuration observations match what the approved source copy owns.
    /// </summary>
    private static void CopyRepositoryControlledSource(string sourceProjectDirectory, string destinationProjectDirectory)
    {
        Directory.CreateDirectory(destinationProjectDirectory);
        string[] projectFiles = Directory
            .EnumerateFiles(sourceProjectDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
            .ToArray();
        Assert.NotEmpty(projectFiles);
        foreach (string projectFile in projectFiles)
        {
            File.Copy(projectFile, Path.Combine(destinationProjectDirectory, Path.GetFileName(projectFile)));
        }

        foreach (string file in Directory.EnumerateFiles(sourceProjectDirectory, "appsettings*.json", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(destinationProjectDirectory, Path.GetFileName(file)));
        }

        foreach (string file in Directory.EnumerateFiles(sourceProjectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceProjectDirectory, file);
            string[] segments = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "bin" or "obj"))
            {
                continue;
            }

            string destination = Path.Combine(destinationProjectDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static async Task RestoreAsync(string projectDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "restore CustomerManagement.Api.csproj --nologo",
                WorkingDirectory = projectDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        Assert.True(process.Start(), "The dotnet restore process could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"dotnet restore failed:{Environment.NewLine}{await output}{Environment.NewLine}{await error}");
    }

    private static void ActivateAndAssert(string outputRoot, DocumentationSetBuildResult built)
    {
        var activation = OutputSetActivator.Activate(outputRoot, built.Files);
        Assert.True(activation.Succeeded, activation.FailureMessage);
        foreach (var file in built.Files)
        {
            Assert.True(
                File.Exists(Path.Combine(outputRoot, file.RelativePath)),
                $"Activated file '{file.RelativePath}' is missing from '{outputRoot}'.");
        }

        Assert.True(File.Exists(Path.Combine(outputRoot, "seqdoc.manifest.json")));
    }

    private static void AssertSameFiles(DocumentationSetBuildResult first, DocumentationSetBuildResult second)
    {
        Assert.Equal(
            first.Files.Select(file => file.RelativePath).Order(StringComparer.Ordinal),
            second.Files.Select(file => file.RelativePath).Order(StringComparer.Ordinal));
        foreach (var file in first.Files)
        {
            var other = Assert.Single(second.Files, candidate => candidate.RelativePath == file.RelativePath);
            Assert.True(
                file.Content.AsSpan().SequenceEqual(other.Content),
                $"Rendered output '{file.RelativePath}' differs between analyses.");
        }
    }

    /// <summary>
    /// Compares two activated output roots byte-for-byte: every expected generated file plus the
    /// ownership manifest. Used both for relocated-root determinism and for the owner verification lane,
    /// with no fingerprint or hash normalization.
    /// </summary>
    private static void AssertSameActivatedOutput(
        string firstRoot,
        string secondRoot,
        IReadOnlyList<RenderedOutputFile> expected)
    {
        foreach (var file in expected)
        {
            string firstPath = Path.Combine(firstRoot, file.RelativePath);
            string secondPath = Path.Combine(secondRoot, file.RelativePath);
            Assert.True(File.Exists(firstPath), $"Activated file '{file.RelativePath}' is missing from '{firstRoot}'.");
            Assert.True(File.Exists(secondPath), $"Activated file '{file.RelativePath}' is missing from '{secondRoot}'.");
            Assert.True(
                File.ReadAllBytes(firstPath).AsSpan().SequenceEqual(File.ReadAllBytes(secondPath)),
                $"Activated file '{file.RelativePath}' differs between '{firstRoot}' and '{secondRoot}'.");
        }

        string firstManifest = Path.Combine(firstRoot, "seqdoc.manifest.json");
        string secondManifest = Path.Combine(secondRoot, "seqdoc.manifest.json");
        Assert.True(File.Exists(firstManifest) && File.Exists(secondManifest));
        Assert.True(
            File.ReadAllBytes(firstManifest).AsSpan().SequenceEqual(File.ReadAllBytes(secondManifest)),
            "Activated ownership manifests differ between output roots.");
    }

    /// <summary>
    /// Resolves the selected graph's RootMethod through the Program Index and asserts the exact
    /// pinned controller action: containing type
    /// <c>CustomerManagement.Api.Controllers.CustomerController</c>, method <c>GetCustomerById</c>,
    /// and exactly one <c>System.Int32</c> value parameter.
    /// </summary>
    private static void AssertRootMethodResolvesThroughProgramIndex(ScenarioGraph graph, ProgramIndexSnapshot index)
    {
        var method = Assert.Single(
            index.Methods,
            candidate => candidate.Id == graph.RootMethod);
        Assert.Equal("GetCustomerById", method.Name);
        var containingType = Assert.Single(
            index.Types,
            candidate => candidate.Id == method.ContainingType);
        Assert.Equal("CustomerManagement.Api.Controllers.CustomerController", containingType.MetadataName);
        var parameter = Assert.Single(method.Parameters);
        Assert.Equal(ParameterRefKind.None, parameter.RefKind);
        Assert.Equal("System.Int32", parameter.FullyQualifiedType);
    }

    /// <summary>
    /// Locates the SeqDoc workspace checkout (the directory containing SeqDoc.slnx) by walking up
    /// from the test output directory. Repository-relative owner evidence paths must resolve against
    /// this root, never against the analyzed external testRepo.
    /// </summary>
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

    private static void AssertSingleH1AndBalancedFences(string relativePath, string content)
    {
        string[] lines = content.Split('\n');
        Assert.True(
            lines.Count(line => line.TrimStart().StartsWith("# ", StringComparison.Ordinal)) == 1,
            $"'{relativePath}' must contain exactly one level-one heading.");
        Assert.True(
            lines.Count(line => line.StartsWith("```", StringComparison.Ordinal)) % 2 == 0,
            $"'{relativePath}' must have balanced code fences.");
    }
}
