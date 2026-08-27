# Issue #21 — Large-Diagram Supplied-Project Acceptance

## Status

Status: `Closed`; final gate: `PassWithKnownBaseline`; review `review0`; I21-F1 fixed in repair rerun 1.
Candidate revision: `WORKTREE`, pending publication. No commit or push.

## Purpose

Generate the largest safe single diagrams for CreditTransfer, FraudManagement, and SMSGateway under deterministic
budgets, measure size and coverage honestly, and prove every emitted Mermaid artifact parses and renders while nothing
exceeds the configured 45,000-character source limit. TicketReservation is the unrelated conservative regression lane.

## Authority and approach

GitHub Issue #21 (parent workstream #2), amended for the current-main publication candidate. Prerequisites #19 and #22
are complete. Use the exact cataloged roots, exact profiles, fresh analyses, measurement, link checks, and Mermaid CLI.

## Target paths (live amendment)

- `src/SeqDoc.Analysis.Roslyn/Semantics/RoslynCallbackBoundaryFactCollector.cs`
- `tests/SeqDoc.Analysis.Tests/CallbackBoundaryProjectionTests.cs`
- `tests/fixtures/AdvancedAnalysis/CallbackBoundaries/CallbackContracts.cs`
- `tests/SeqDoc.Rendering.Tests/packages.lock.json`
- `docs/examples/fraud-management.yaml`
- `docs/examples/sms-gateway.yaml`
- `docs/work/traversal/I21/**`

Ignored run artifacts and generic product-repair escape owners are not target paths.

## Non-goals

New framework semantics, overview/child decomposition (#23), wording, CI/packaging, and corpus-wide metrics beyond the
named acceptance set plus TicketReservation.

## Existing coverage and budget

Reuse the existing callback tests and Rendering lane. The Test Writer audit found no gap: Analysis 25/25 closes the
callback risk, and Rendering 60/60 covers the lock/rendering risk. Add no tests. Record one measurement table per
application and one regression note.

## Risk inventory

1. External solution fails to restore/load partially; preserve conservative diagnostics.
2. A diagram silently exceeds 45,000 characters; verify enforcement.
3. Large nested fragments produce invalid Mermaid; render every file.
4. Guarded-topology composition regresses accepted output; compare shape, not unavailable old bytes.
5. Cataloged MethodId/profile drift; use identical profile and root flags.
6. Unrelated projects show false-positive admission; TicketReservation remains conservative.

## Current-main acceptance evidence

- SeqDoc Release solution restore/build passed with 0 warnings/errors.
- Approved external temporary artifacts remained outside the repository. Exact revisions: CT
  `02b82a5115ef6e2d138c70670f28b959fb646f6e`; Fraud
  `7aabfef98fa4d47781bd8a98b9061ddcafb88836`; SMS
  `7ca797356b1856eb815922ca977e9d85a569cb84`; Ticket
  `1e25b6943a7dcfc443b8dca2ea946ee28afe811f`.
- Direct target builds passed: CT Web with known source/package warnings; Fraud WindowsService clean; SMS WindowsHost
  with known CoreWCF package advisories; Ticket API clean.
- Two fresh analyses per lane produced byte-identical manifests/files, 0 broken links, and no file over 45,000 chars:
  CT project Web profile `3f0c16b6`, fingerprint `dd9b9153`, files18/mmd8/max4821/messages100; Fraud target
  `FraudManagement.sln` profile `f874be7e`, fingerprint `f9a36fd5`, files74/mmd36/max1227/messages108; SMS WindowsHost
  profile `a82776da`, fingerprint `f1bfaa65`, files32/mmd15/max1907/messages95; Ticket API profile `417880f3`,
  fingerprint `0e5e3ea2`, files10/mmd4/max484/messages17.
- The initial Fraud WindowsService run failed `SD4011` because accepted roots belong to the solution profile. Catalog/profile
  checking identified the exact solution target and corrected the command; no config drift occurred.
- Mermaid CLI 11.16.0 rendered all 63 run-1 Mermaid files to SVG with 0 failures. Accepted diagnostics were conservative
  warnings only, with no analysis errors. Known omitted-diagnostic-artifact wording is separate I24 behavior.
- Current-main output has the historical accepted diagram counts/maxima. Repeated current-main bytes are authoritative;
  no byte equality to unavailable old artifacts is asserted.

## Focused verification and final gate

Focused verification passed Analysis 25/25 and Rendering 60/60. The locked Release solution restore and build passed
with 0 warnings/errors. The complete Analysis invocation first exposed missing standalone fixture restores, not
candidate defects: PassC CoreWcfServices, GetMeaning, DecisionTopology, FourFlows, and MediatRDispatch. After the
exact locked fixture restores and setting `SEQDOC_TEST_PROJECTS_ROOT` for the supplied corpus, Analysis passed 235/235;
no source or test changes were made during that command correction sequence.

The final Release gate passed with the known unchanged baseline: Core 91/91, Analysis 235/235, Behavior 63/63,
Scenarios 218/218, Wording 116/116, Rendering 60/60, and Acceptance 30/31. The sole Acceptance failure is
`CorpusMediatRTests.OrderingDraftRouteReachesExactMediatRHandlerWithoutPipelineClaim`; the candidate does not touch
MediatR or Acceptance, and project status already records this baseline lane. `git diff --check` also passed on the
unchanged candidate.

External acceptance remains passed: the four lanes were byte-identical across two runs, all 63/63 Mermaid files
rendered, Markdown links resolved, no artifact exceeded its budget, and diagnostics matched the recorded conservative
baseline.

## Independent review disposition

Independent review ran once (`review0`); no second review. Repair rerun 1 records I21-F1 **Fixed**: the measurement packet now binds
run parents, `output` directories, and `cache-v1.db` files for every lane, explicitly omits configuration for the Ticket
HTTP-only lane, and supplies executable audit and Mermaid-render commands. The affected documentation/audit verification
passed against the existing 63-file current-main outputs. Product-focused tests were not repeated because the candidate
and tests are unchanged.
