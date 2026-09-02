# Exact direct outbound HTTP boundaries checkpoint

## State

`Building`

Issue #54 is the frozen semantic contract. Owner continuation authorization and current-main readiness PASS are at
https://github.com/Bilaltariq41/SeqDoc/issues/54#issuecomment-5509514090. The accepted baseline is
`aec7cade98dcfe55e9e2b8eadf9c0d42055254ff`; PR #59 preserves Qais's candidate commit
`7a6dbde5b28bab4658c4ee0025b7847e368ca0c8`.

## Objective

Admit only exact compiler-selected `System.Net.Http.HttpClient.GetAsync(string)` and
`PostAsync(string, HttpContent)` calls for the frozen net9/net10 assembly identities and carry one root-local typed
outbound-request fact through Scenario Graph and Documentation Plan to conservative Markdown/Mermaid wording. Never
display URI/content/credentials or claim response, success, remote execution, retry, or matching server behavior.

## Target paths

The complete 21-path PR #59 file list is frozen:

- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `src/SeqDoc.Cli/AggregateAnalysisBuilder.cs`
- `src/SeqDoc.Core/Frameworks/OutboundHttpFacts.cs`
- `src/SeqDoc.Core/ScenarioGraph/ScenarioGraphContracts.cs`
- `src/SeqDoc.FrameworkModels/OutboundHttp/HttpClientOutboundModel.cs`
- `src/SeqDoc.FrameworkModels/OutboundHttp/HttpClientOutboundModelDiagnostics.cs`
- `tests/SeqDoc.Analysis.Tests/OutboundHttpProjectionTests.cs`
- `tests/SeqDoc.Cli.Tests/OutboundHttpCliTests.cs`
- `tests/SeqDoc.Core.Tests/OutboundHttpContractCompatibilityTests.cs`
- `tests/SeqDoc.FrameworkModels.Tests/OutboundHttp/HttpClientOutboundModelTests.cs`
- `tests/SeqDoc.Scenarios.Tests/OutboundHttpScenarioTests.cs`
- `tests/SeqDoc.Wording.Tests/OutboundHttpWordingTests.cs`
- `tests/fixtures/BehaviorDocumentation/OutboundHttp/**`
- `tests/fixtures/BehaviorDocumentation/OutboundHttpLookalike/**`
- `docs/project/work-items/GH-52.json`
- `docs/project/work-items/GH-54.json`
- `docs/work/outbound-http/QHTTP-A/**`

No additional path is authorized without owner amendment.

## Non-goals

- No external acceptance (#53), URI/content/header extraction, response/status/outcome, DI/factory/resilience support,
  client/server correlation, persistence, worker, WCF/CoreWCF, MediatR, configuration, or persistence change.
- No application-name matching, public numeric renumbering, persistence/serialization change, or generic platform-call
  inclusion.
- No rewrite/rebase of Qais's existing commit.

## Risk inventory

1. Partial identity or missing admission fields produce a positive fact or unsupported diagnostic.
2. Wrong assembly/token/version, foreign same-qualified type, unsupported overload, or supplied ordinal crosses admission.
3. Diagnostic identity changes when only explanatory reason changes.
4. Duplicate/conflicting facts or incompatible profile/fingerprint/caller/operation joins emit a boundary.
5. Platform calls duplicate generic DirectExact presentation.
6. Guard/topology placement is invented or SC013 shape changes.
7. Evidence/certainty weakens, ordering becomes unstable, or cancellation is ignored.
8. URI, credential, content, response, status, success, retry, or remote-completion wording leaks.
9. Current-main Scenario/Planner shared-path behavior regresses.
10. Candidate tests prove hand-built facts without a real Roslyn-to-CLI observable path.

## Existing relevant coverage

PR #59 adds ~24 candidate `[Fact]` tests (the `FullyQualifiedName~OutboundHttp` focused filter additionally exercises pre-existing shared-lane tests, so it reports more), including real Roslyn,
foreign-assembly, unsupported overload/version, conflict, topology, credential, deterministic output, and contract
compatibility coverage. Existing current-main CoreWCF failures are baseline evidence only.

## Test Writer and soft budget

The existing candidate already includes the required semantic Test Writer coverage. Repair only the uncovered Copilot
risks: missing/default supplied ordinals, reason-independent diagnostic identity, and matching version-behavior docs.
Add at most 2 focused assertions/tests; do not duplicate the 24-test suite or edit unrelated tests.

## Focused verification command

```powershell
dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"
```

## Review boundary

Qais must merge current main, resolve the three current Copilot findings in one bounded repair, run the focused command,
and stop at `ReviewRequired`. The Orchestrator then inspects the complete candidate and invokes one independent Reviewer.
Record every disposition and run the final gate only after findings are resolved.

## Final gate

```powershell
dotnet build SeqDoc.slnx -c Release; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release --no-build
```

## Repair round 2 (Copilot findings, post-activation)

State: `ReviewRequired` pending Orchestrator inspection and independent Reviewer.

| Finding | Disposition | Production repair | Test | Observable assertion | Residual |
| --- | --- | --- | --- | --- | --- |
| 1. Missing `SuppliedParameterOrdinals` projection coerced to empty array -> spurious `SEQHTTP001` (`MismatchedSuppliedOrdinals`) | Fixed | `HttpClientOutboundModel.cs` AnalyzeOperationAsync: after family-recognition and `requiredVersion is null` checks, added guard `if (operation.SuppliedParameterOrdinals.IsDefault) return ModelResult.Unrecognized;` then `var ordinals = operation.SuppliedParameterOrdinals;` (removed empty-array coercion). Present-but-wrong / present-but-empty ordinal sets still reach `SEQHTTP001` unchanged. | Extended `PartialIdentityForeignLookalikeAndMissingRequiredFieldsFailClosedSilently` with `missingOrdinals` case | `Assert.Empty(missingOrdinals.Facts)` AND `Assert.DoesNotContain(... d.Code == DiagnosticCode)` | none |
| 2. `SEQHTTP001` identity included `reasonText` (`subjectId = operationId + US + reasonText`), contradicting file contract | Fixed | `HttpClientOutboundModelDiagnostics.cs`: `var subjectId = operationId;` (profileId already a separate `DiagnosticIdentityDescriptor` field). `reasonText` retained in `internalDetail` (`reason={reasonText}`). Code, severity, summary, messages, internalDetail format unchanged. | Added `Seqhttp001IdentityIsReasonIndependentForTheSameProfileAndOperation` (driven through the model: `internal` codes not visible to test project, no `InternalsVisibleTo`) | Two operations, same `operation.Id`, reasons send-async vs wrong-return: `Assert.Equal(first.Id, second.Id)` and `Assert.NotEqual(first.InternalDetail, second.InternalDetail)` | none |
| 3. XML doc claimed "version-mismatched identity stays silent", contradicting the emitted `SEQHTTP001` | Fixed | `HttpClientOutboundModel.cs` `<summary>`: replaced the "partial, foreign, or version-mismatched identity stays silent" sentence with accurate wording — wrong/missing assembly version on an applicable net9/net10 profile emits one `SEQHTTP001`; only failed family recognition, non-net9/net10 profile, or missing supplied-ordinal projection stays silent. | Doc-only; covered by existing `AtomicProfileAssemblyVersionCrossingEmitsWrongAssemblyVersionSeqHttp001AndNoFact` + new fix-1 test | n/a | none |

No existing assertion required changing. No soft-budget exception (2 assertions added; one extends an existing test, one new `[Fact]`).

Focused verification (build, no `--no-build`), all `FullyQualifiedName~OutboundHttp`:

| Project | Passed | Failed |
| --- | --- | --- |
| SeqDoc.Core.Tests | 2 | 0 |
| SeqDoc.FrameworkModels.Tests | 7 | 0 |
| SeqDoc.Analysis.Tests | 5 | 0 |
| SeqDoc.Scenarios.Tests | 4 | 0 |
| SeqDoc.Wording.Tests | 4 | 0 |
| SeqDoc.Cli.Tests | 3 | 0 |

Total 25 (was 23 matching the filter; +2 this round). Final gate NOT run (deferred to Orchestrator per Review boundary).

## Independent review (reviewer-medium, post-activation repair) — dispositions

Verdict: **Accept as-is.** Zero Blocking/Major/Minor findings. Two Observations, both dispositioned without code change:

| Finding | Severity | Disposition |
|---|---|---|
| Capsule "24 focused tests" narrative imprecise vs the FQN-substring focused filter (matches 23 pre-repair / 25 now, including pre-existing shared-lane tests whose names contain "OutboundHttp"). All six projects `Passed!` with `Skipped: 0`; every candidate test file compiled; no intended test lost or undiscovered. | Observation | Fixed — coverage sentence in "Existing relevant coverage" reworded; no code change. |
| `OperationDescriptor.SuppliedParameterOrdinals` defaults to `default` (`IsDefault == true`). Fix 1's guard correctly fails closed silently on that. The sole production producer `FrameworkAnalysisRequestProjector.ProjectSuppliedParameterOrdinals` always returns a non-default array (`[]` or a populated ascending set), so no real admissible call is silenced. Residual: a hypothetical future second `OperationDescriptor` producer that forgets to project this field would silently drop admissible calls. | Observation | Accepted; out of issue #54 scope. Noted for the maintainer as a possible projector-side assert / record-field comment in future work. |

Repair-round-2 findings 1–3 (Copilot) verified correctly and completely fixed with no weakened assertions; the merge of current `main` is sound and non-destructive (contributor commit `7a6dbde` preserved as an ancestor); scope is exactly the 3 authorized files plus this capsule. All five `AGENTS.md` proof gates hold on the complete candidate.

Focused verification (build, `--filter FullyQualifiedName~OutboundHttp`): Core 2/2, FrameworkModels 7/7, Analysis 5/5, Scenarios 4/4, Wording 4/4, Cli 3/3 — 25/25. `dotnet build SeqDoc.slnx -c Release` 0 warnings / 0 errors. Final 7-suite gate: pending (findings now resolved).
