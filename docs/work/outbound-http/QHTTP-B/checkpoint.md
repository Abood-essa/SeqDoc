# FraudManagement direct outbound HTTP acceptance checkpoint

## State

`NotStarted`

GitHub Issue #53. The frozen implementation baseline is merged PR #59 at
`0b8e4b7a91cf52e4a98542bcc307f9262414efdf`.

## Objective

Provide acceptance-only proof that the frozen direct `HttpClient` GET/POST semantics reach the production CLI and
visible conservative Markdown/Mermaid output for FraudManagement revision
`7aabfef98fa4d47781bd8a98b9061ddcafb88836`, `Release/net9.0`, using exactly these roots:

- `AddComplaint` — `method:v1:c3310b12f1a331d7ee9871a964209e89da0a0dcb84b086e4b62cbbbdc2a66417`
- `Lookups` — `method:v1:b7a44d4b1128669b35cda87326e73098991a24dbd0b975b9986c9050b8b45504`

The proof must remain conservative: it may establish only the compiler-evidenced direct GET/POST boundary. It must
not claim URI, content, headers, credentials, status, success, retry, remote execution, or other request/outcome
details.

## Target paths

The exact writable implementation path is:

- `tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs`

Checkpoint evidence may be recorded only under:

- `docs/work/outbound-http/QHTTP-B/**`

No other path is authorized. In particular, do not edit `src/**`, semantics, framework/scenario/planner/wording/
rendering contracts, fixtures, checked-in configuration, build/workflow files, or external-project source.

## Non-goals

- No production semantic repair or contract change.
- No claims about URI/content/header/credential/status/success/retry/remote execution.
- No fixture, configuration, build, workflow, or external-source changes.
- No application-name matching, invented evidence, or strengthened certainty.

## Risk inventory

1. The lane uses the wrong external revision, project, TFM, profile, or root fingerprint.
2. Stale output, dependency state, or cache produces false evidence.
3. GET or POST is falsely positive, missing, duplicated, or confused with a generic call.
4. URI, content, credentials, request values, or remote outcome leak into artifacts.
5. Evidence or certainty is strengthened beyond the frozen compiler semantics.
6. Required Markdown/Mermaid output is missing or invalid, links break, budgets are exceeded, or Mermaid fails.
7. Repeated clean runs are nondeterministic.
8. Acceptance pressure causes an unauthorized semantic repair or conceals identity drift.

## Existing coverage and test plan

Merged PR #59 supplies producer-to-CLI focused coverage for the frozen semantics. Reuse the isolated-worktree and
artifact-validation patterns from `ServiceClientExternalCorpusTests.cs`. The Test Writer must add only the minimum
acceptance-critical coverage in the sole writable test file for external identity, false-positive boundaries,
evidence/certainty, and deterministic output. Group the checks under one expensive fixture and use 2–4 distinct tests
within that soft budget; do not duplicate producer or unit assertions already covered by PR #59.

## Focused verification

Set `SEQDOC_TEST_PROJECTS_ROOT` to the supplied corpus and run:

```powershell
$env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path "../SeqDoc-TestProjects").Path; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~OutboundHttpExternalCorpus"
```

The lane must use FraudManagement revision `7aabfef98fa4d47781bd8a98b9061ddcafb88836`, `Release/net9.0`, and the
two exact roots above. Stop on identity drift or any required semantic change.

## Completion assertions

- The production CLI runs the pinned FraudManagement lane and visibly presents conservative direct GET and POST
  boundaries in Markdown/Mermaid.
- No sensitive/request values or remote outcome claims occur in the artifacts.
- Artifacts are complete, valid, linked, within budget, and Mermaid-valid.
- Two clean repeated runs are byte-identical.

## Review boundary

Stop at `ReviewRequired` after implementation and focused verification. Run one independent review, record every
finding as `Fixed`, `Rejected` with evidence, or `Deferred` with explicit owner approval, then run the final gate.
Acceptance pressure does not authorize semantic repair.

## Final gate

After the independent review and findings resolution, run the complete AcceptanceTests Release suite once:

```powershell
$env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path "../SeqDoc-TestProjects").Path; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release
```
