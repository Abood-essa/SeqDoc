# Issue 41 — Admit measured classic-WCF net9 compatibility tuples

## Status

Closed (final gate Pass). Candidate on branch issue-41-classic-wcf-net9-tuples, WORKTREE, pending publication. No commit, push, or PR.

## Purpose

`CoreWcfServiceModel` admits exactly one classic-WCF identity tuple — `System.ServiceModel.Primitives` assembly version `8.1.2.0` paired with the `System.Runtime` `10.0.0.0` `GeneratedCodeAttribute` marker. Realistic net9 generated `ClientBase<TContract>` clients on other supported versions fail closed silently before producing `ServiceClientBoundaryFact` / `ServiceClientInvocationFact`. Widen admission to an explicit, atomic set of measured compatibility tuples so those clients reach one visible outbound-client message. Every unsupported or mixed identity still fails closed.

## Measured compatibility tuples (via MSBuildWorkspace `ContainingAssembly.Identity`)

| Tuple | TFM | `System.ServiceModel.Primitives` asm version | `GeneratedCodeAttribute` container (`System.Runtime`) asm version | PKT | Disposition |
|---|---|---|---|---|---|
| Existing fixture | net10.0 | `8.1.2.0` | `10.0.0.0` | `b03f5f7f11d50a3a` | Preserve unchanged |
| CreditTransfer | net9.0 | `8.0.0.0` | `9.0.0.0` | `b03f5f7f11d50a3a` | Add |
| SMS UI Web | net9.0 | `8.1.0.0` | `9.0.0.0` | `b03f5f7f11d50a3a` | Add |

Each tuple is **atomic**: the `System.ServiceModel.Primitives` version and the `System.Runtime` marker version are bound together. The contract/operation/fault attribute identity, the `ClientBase\`1` identity, and the constructed-contract identity must all resolve to one supported tuple; the `GeneratedCodeAttribute` marker check must then use that same tuple's marker version. No independent version lists, no cross-product, no major-version range, no minimum version, no package-name or namespace heuristic.

Attribute metadata names (unchanged): `System.ServiceModel.ServiceContractAttribute`, `System.ServiceModel.OperationContractAttribute`, `System.ServiceModel.FaultContractAttribute`. `ClientBase` metadata name: `System.ServiceModel.ClientBase\`1`. `GeneratedCodeAttribute` metadata name: `System.CodeDom.Compiler.GeneratedCodeAttribute`. The CoreWCF family (`CoreWCF.Primitives 1.9.0.0`) is out of scope and stays exactly as-is.

## Target paths (allowlist)

- `src/SeqDoc.FrameworkModels/CoreWcf/CoreWcfServiceModel.cs`
- `tests/SeqDoc.FrameworkModels.Tests/CoreWcf/CoreWcfServiceModelTests.cs`
- `tests/SeqDoc.FrameworkModels.Tests/CoreWcf/CoreWcfTestIndexFactory.cs`
- `tests/SeqDoc.Analysis.Tests/CoreWcfServiceModelProjectionTests.cs`
- `tests/SeqDoc.Analysis.Tests/CoreWcfClientInvocationProjectionTests.cs`
- `tests/fixtures/PassC/ClassicWcfNet9Compatibility/**` (new; the implementation plan must name every `.csproj`, source file, and `packages.lock.json`)
- `docs/work/services/I41/**`

Any other path — including `Directory.Packages.props`, `CoreWcfClientInvocationScanner.cs`, Scenario/Wording/Rendering/CLI/config, `SeqDoc.Core` — requires the Orchestrator to obtain owner approval first. Stop and ask.

## Non-goals

- Version ranges, minimum versions, package-name/namespace heuristics, or independent cross-product version sets.
- Any change to `CoreWcfClientInvocationScanner`, `SeqDoc.Core` facts, Scenario Graph, `DocumentationPlanner`, wording, or rendering.
- Editing `Directory.Packages.props` — use project-local `VersionOverride` in the new fixture project(s).
- The Issue #8 acceptance tests, external-corpus analysis, or `docs/examples/*.yaml` — that is Issue #8, blocked on this.
- CoreWCF-family behavior.

## Risk inventory

1. Accidental major-version admission (matching on major only, or `StartsWith`).
2. Cross-product admission — an `8.0.0.0` ServiceModel contract accepted with a `10.0.0.0` marker because the two version checks are independent.
3. Generated-marker mismatch not caught → `GeneratedClient`/`SourceClient` misclassified or a fact admitted under the wrong tuple.
4. The existing `(8.1.2.0, 10.0.0.0)` net10 tuple regresses.
5. Profile / project / Program Index fingerprint leakage — a fact admitted across a profile boundary.
6. A producer fact is created but never reaches a visible Markdown/Mermaid message.
7. A fixture that only builds and does not exercise the real Roslyn → model → scenario → planner path.
8. Evidence or effective certainty strengthened rather than preserved as the weakest contributor.
9. An unsupported identity stops failing closed (silent) and instead produces a message.
10. `System.ServiceModel.Primitives` `8.0.0` and `8.1.0` cannot coexist in one fixture project → separate fixture projects, each with its own locked `packages.lock.json`.

## Existing coverage and soft budget

`CoreWcfServiceModelTests`, `CoreWcfClientInvocationModelTests`, `CoreWcfServiceModelProjectionTests`, `CoreWcfClientInvocationProjectionTests`, and existing Scenario/Wording client tests pin the current `8.1.2.0`/net10 behavior. `CoreWcfClientInvocationProjectionTests` already has `RealFixtureCallSiteProducesExactlyOneVisibleClientInvocationMessageThroughScenarioAndPlanner` — add the net9-tuple producer-to-observable sibling there. Soft budget: **6–10** distinct new claims. More than 10 needs a written risk-by-risk justification in the plan.

## Focused command

```
dotnet restore tests/fixtures/PassC/ClassicWcfNet9Compatibility/<project>.csproj --locked-mode
dotnet restore tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj
dotnet restore tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj
dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CoreWcf"
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CoreWcfClient|FullyQualifiedName~CoreWcfServiceModelProjection"
```

## Final gate

```
locked Release solution restore + build
dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --no-restore
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-restore
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-restore
dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-restore
dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-restore
git diff --check
```

Run once after independent-review findings are resolved.

## Environment

`~/.dotnet` toolchain: .NET SDK 9.0.317 + 10.0.302, runtimes 9.0.19 and 10.0.9/10/11. Set `DOTNET_ROOT=$HOME/.dotnet`, prepend `$HOME/.dotnet` to `PATH`, `DOTNET_MULTILEVEL_LOOKUP=0`. The net9 fixture(s) require this toolchain to resolve `net9.0` reference assemblies.

## Log

- Created from the owner-approved SeqDoc-Issue-8 game plan. Issue #41 filed (`github.com/Bilaltariq41/SeqDoc/issues/41`), amended Issue #8 contract posted. Delegated to builder-medium; stops at `ReviewRequired`.
### Independent review (review0) — repairs

Verdict: ACCEPT WITH MINOR REPAIRS (no Blocking/Major). All four applied on `issue-41-classic-wcf-net9-tuples`.

| Finding | Disposition | What changed | Test | Residual |
|---|---|---|---|---|
| I41-F1 (Minor) | Fixed | No production change (coarse `HasClientBase` gate left as-is; accepted conservative behavior). Added a same-shaped negative test. | `CoreWcfServiceModelTests.Net9TupleClientBaseWhoseContractDoesNotResolveFailsClosedWithConservativeDiagnosticAndNoFacts` — `ClientBase<T>` at `8.0.0.0` with a contract carrying no `[ServiceContract]`: `AnalyzeSymbol` emits no `ServiceClientBoundaryFact`/`ServiceClientInvocationFact`, `Recognized == false`, single `SEQWCF001` (`EligibilityShapeUnavailable`) diagnostic; `AnalyzeClientInvocation` for the same shape returns Unrecognized (no fact, no diagnostic). | None. |
| I41-F2 (Minor) | Fixed | No production change. Added a weakest-certainty boundary test. | `CoreWcfServiceModelTests.Net9TupleClientBoundaryNeverStrengthensADegradedTriggeringCertainty` — net9 `(8.0.0.0, 9.0.0.0)` generated client with `Conservative` triggering certainty: emitted `ServiceClientBoundaryFact.Certainty` is `Conservative`, asserted `!= Exact`. Reuses `AnalyzeNet9ClientAsync`'s `certainty` parameter. | None. |
| I41-F3 (Observation) | Fixed | `CoreWcfServiceModel.cs` `ClassicWcfCompatibilityTuple` XML doc reworded: the discriminator is plainly stated as assembly simple name + exact assembly version + metadata name; PKT `b03f5f7f11d50a3a` retained only as an informational note ("the measured assemblies also carry PublicKeyToken ..."), no longer reading as an enforced check. | Covered by existing identity tests; doc-only. | None. |
| I41-F4 (Observation) | Fixed | `CoreWcfServiceModel.cs` `AnalyzeMethod` client branch: added a comment above the `clientMembers[0].Admitted` call site stating `clientMembers` is tuple-homogeneous by construction (one base type; `IsClientBaseDerivedForContract` requires the `ClientBase<T>` argument to equal `member.InterfaceType`), so `[0]`'s marker version applies to all. Comment only (`System.Diagnostics` not imported). | n/a (comment). | None. |

Verification after repairs: `dotnet test tests/SeqDoc.FrameworkModels.Tests --filter FullyQualifiedName~CoreWcf` → 53/53 passed (was 51; +2 for F1/F2). `dotnet build SeqDoc.slnx -c Release` → 0 warnings, 0 errors. `git diff --check` → clean. Full final gate deferred to the orchestrator.

- builder-medium implemented the atomic tuple set in `CoreWcfServiceModel` (`Identity.ClassicWcfCompatibilityTuple` + `ClassicWcfCompatibilityTuples`, private `AdmittedContract` carrying the resolved ClientBase + generated-marker versions; `TryGetAdmittedContract`/`ResolveContracts` enforce one shared tuple across ServiceContract/OperationContract/FaultContract; `MatchesAdmitted*`, `IsClientBaseDerivedForContract`, `IsGeneratedCodeMarker` all thread that one tuple). Two net9.0 fixture projects added under `tests/fixtures/PassC/ClassicWcfNet9Compatibility/` (8.0.0 + 8.1.0, project-local `VersionOverride`, committed `packages.lock.json`). Marker version 9.0.0.0 for both net9 tuples confirmed through the real MSBuildWorkspace producer. Focused command green: FrameworkModels `~CoreWcf` 51/51, Analysis `~CoreWcfClient|~CoreWcfServiceModelProjection` 19/19; Release solution build 0/0; `git diff --check` clean. Status -> ReviewRequired.

## Final gate (run once, post-repair)

Locked Release restore + `dotnet build SeqDoc.slnx -c Release`: 0 warnings / 0 errors.
- FrameworkModels.Tests 302/302
- Analysis.Tests 239/239
- Scenarios.Tests 218/218
- Wording.Tests 116/116
- Rendering.Tests 77/77
- Core.Tests 91/91
- Behavior.Tests 63/63
- `git diff --check` clean

The first Analysis.Tests run showed one failure, `ProgramIndexBuilderTests.TicketReservationLoadsThroughBaselineIndexerWhenAvailable` — an external-corpus test not touched by the I41 diff (only `CoreWcfClientInvocationProjectionTests.cs` changed in that project), failing because the `TicketReservation.sln` corpus checkout was not restored. After `dotnet restore ../SeqDoc-TestProjects/Provided/TicketReservation-Solution/TicketReservation.sln` the test passes. Not an I41 regression.

## Closure

Independent review (`reviewer-medium`, `review0`): ACCEPT WITH MINOR REPAIRS — no Blocking/Major. Findings I41-F1..F4 all Fixed on this branch (repair trace above). Final gate passed. Checkpoint Closed. The candidate remains uncommitted on branch `issue-41-classic-wcf-net9-tuples`; pushing it and opening a PR that closes GitHub Issue #41 against `upstream/main` is an owner decision, not performed automatically.
