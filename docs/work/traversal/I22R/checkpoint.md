# Issue 22 post-review repair — occurrence identity and production proof

## Status: Closed

The repair is included in the Issue #22 publication candidate. Focused verification, the final Release gate, and
production nested guarded Mermaid verification passed; Issue #22 is closed and publication is next.

Baseline revision: `fdc63f6` (commit 3, accepted guarded-topology review candidate).
Candidate revision: `7212d56` (commit 4, occurrence identity and production proof repair).

## Purpose

Resolve the two acceptance gaps found by the independent whole-wave review before Issue #22 publication: preserve
occurrence identity through Diagram Plan fragment/arm identities, and prove realistic source reaches the production
Roslyn → Behavior → Scenario → Documentation/Mermaid path.

## Target paths

- `src/SeqDoc.Core/ScenarioGraph/ScenarioGraphContracts.cs`
- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `tests/SeqDoc.Scenarios.Tests/DirectExactTraversalTests.cs`
- `tests/SeqDoc.Rendering.Tests/GuardedCalleeMermaidRenderingTests.cs`
- `tests/SeqDoc.AcceptanceTests/BehaviorDocumentationFourFlowTests.cs`
- one narrow new C# fixture under `tests/fixtures/BehaviorDocumentation/FourFlows/Services/`
- `docs/work/traversal/I22R/**`

Changing another product or test path requires an explicit recorded reason before the change.

## Required behavior

1. A callee-local `ScenarioDecision` carries its exact direct-call occurrence scope as an optional typed property.
   Root/service decisions keep null scope and remain byte-identical.
2. Documentation fragment, arm, break, branch, and element identities include occurrence scope only when it is
   present. Repeated calls to the same guarded method with the same condition operation receive distinct stable
   Diagram identities; legacy null-scope vectors and output remain unchanged.
3. A realistic fixture contains an exact configured root calling a source-available guarded child whose guarded arm
   calls another source-available method. Production Roslyn extraction and Behavior analysis must supply the bodies,
   calls, anchors, decisions, predicates, and control dependences used by Scenario composition.
4. The realistic path reaches DocumentationPlanner and Mermaid rendering. The guarded message appears exactly once
   in the correct fragment arm with non-empty evidence and least certainty. An unsupported local loop or exception
   shape remains diagnostic-only and never becomes an unconditional message.
5. Reversed eligible inputs and repeated construction remain deterministic. No identity input uses labels, source
   text, checkout paths, timestamps, budgets, or enumeration order.

## Non-goals

No new loop/switch/exception semantics, framework semantics, persistence, worker behavior, CLI changes, external-corpus
reruns, public configuration changes, or publication-history reconstruction.

## Risk inventory

1. Changing every fragment key would churn accepted root output; null scope must retain the exact legacy path.
2. Planner-only duplicate ordinals would couple identity to visual order; occurrence identity must be carried from
   the Scenario seam.
3. A hand-built request could leave producer incompatibility hidden; the acceptance test must begin with C# source.
4. Repeated fragment/arm ids or keys could conflate debug, branch, or rendering behavior.
5. Unsupported topology could be flattened rather than withheld.
6. Evidence or certainty could be strengthened between compiler facts, memberships, and Diagram elements.

## Existing coverage

- `DirectExactTraversalTests` proves occurrence-scoped Scenario topology, repeated shared callees, polarity,
  chronology, evidence, certainty, and reversed-input determinism using hand-built facts.
- `GuardedCalleeMermaidRenderingTests` proves one hand-built nested guarded shape renders through the production
  Scenario/Documentation/Renderer consumers.
- `BehaviorDocumentationFourFlowTests.BuildAsync` performs production Roslyn extraction, Behavior analysis,
  framework analysis, Scenario construction, and Documentation planning for a realistic fixture.
- Stable identity tests lock the null-scope legacy decision vector.

## Soft test budget

Four focused claims: distinct repeated-occurrence Diagram structural identities; legacy null-scope identity/output;
one realistic source-to-Mermaid guarded placement; one realistic unsupported-shape conservative boundary. Extend
existing tests rather than duplicating generic fragment or traversal coverage.

## Focused command

```powershell
dotnet build tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release && dotnet build tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release && dotnet build tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release && dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~DirectExactTraversal" && dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~GuardedCallee" && dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~BehaviorDocumentationFourFlow"
```

## Final gate

After independent review findings were resolved, the Release gate passed: Core 84/84, Analysis 198/198, Behavior
63/63, Scenarios 175/175, Wording 112/112, Rendering 60/60, and Acceptance 29/30. The sole Acceptance failure is
`CorpusMediatRTests.OrderingDraftRouteReachesExactMediatRHandlerWithoutPipelineClaim`, a known baseline failure
because MTR-1 is not present on `origin/main`; that repair was not imported or claimed. The aggregate result is 721
passed and one known-baseline failure. The first Analysis attempt had 14 fixture-reference failures before locked
fixture restores; the rerun passed 198/198.

Production nested guarded Mermaid verification also passed: 3 messages, 405 characters, zero in-process validator
findings, and a 26,209-byte SVG from Mermaid CLI.

## Review-repair capsule

The repair round covers `I22R-F1`, `I22R-F2`, and `I22R-R1` through `I22R-R5` in `state.json`:

- compute topology pruning from the complete transitive withheld-occurrence closure;
- inspect unsupported loop and exception child anchors even when a callee has no ordinary decision;
- prove the realistic compiler-to-Diagram path at each changed typed seam, including exact calls/anchors, decisions,
  predicates, control dependence, evidence, and weakest certainty;
- encode Diagram identity tuple components unambiguously rather than concatenating raw condition and occurrence text;
- cover missing/conflicting anchors, loop-body versus block membership, exception placement, and unaffected siblings.

Target paths and non-goals remain unchanged. Recognized unsupported placement stays diagnostic-only. Principal risks
are stale descendant topology, flattened unsupported descendants, identity collisions, strengthened claims, sibling
over-pruning, and tests that prove only hand-built consumers. Focused verification recorded in this candidate is
DirectExactTraversal 45/45, GuardedCallee 3/3, and FourFlows 13/13 passed.
