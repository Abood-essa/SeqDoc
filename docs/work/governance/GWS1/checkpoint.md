# GWS1 canonical work-state checkpoint

## State

`Closed`

Owner activation was granted on 2026-09-01. This checkpoint replaces duplicated current-state authority with one
versioned work-item registry and generated projections. It does not change SeqDoc product behavior.

The canonical record is `docs/project/work-items/GWS1.json`; this capsule is the implementation contract, not a second
lifecycle authority.

## Objective

Make `docs/project/work-items/**` the sole current-state authority for every GitHub workstream, implementation issue,
acceptance issue, and maintainer checkpoint. Represent readiness, activation, review, repair, verification, closure,
ownership, dependencies, contract revision, baseline, checkpoint capsule, active PR, and local Orchestrator selection in
one typed record per work item.

Generate or validate all other views from those records:

- `docs/project/execution.json` is the selected-workspace projection;
- GitHub lifecycle labels are remote projections;
- `docs/project/status.md` remains historical narrative, not current authority;
- `docs/project/parallel-workstreams.md` remains explanatory topology, not current authority;
- checkpoint capsules remain implementation contracts, not independent lifecycle state.

Migrate every existing GitHub issue in the repository plus this maintainer checkpoint. Preserve the distinction between
`Ready` and `Active`, but store it once. Multiple contributors may have `Active` items; at most one item may be selected
for a given Orchestrator execution projection, and zero selected items represent an idle Orchestrator.

## Canonical lifecycle

`Draft`, `Blocked`, `Ready`, `Active`, `ReviewRequired`, `ResolvingFindings`, `Verifying`, `Closed`, `Cancelled`.

Only `Ready` or `Active` authorizes product implementation. `Ready` authorizes the named public contributor to create a
branch from the frozen baseline. `Active` records that implementation has started. The selected execution item tells the
local root Orchestrator which one item it may operate on; it does not invalidate other contributors' active items.

## Target paths

Canonical state and projections:

- `docs/project/work-state.schema.json`
- `docs/project/work-items/**`
- `docs/project/execution.json`
- `docs/project/status.md`
- `docs/project/parallel-workstreams.md`

Policy and contributor guidance:

- `AGENTS.md`
- `docs/README.md`
- `docs/project/workflow.md`
- `docs/project/issue-readiness.md`
- `docs/project/delegated-contribution-workflow.md`
- `.github/ISSUE_TEMPLATE/implementation.yml`
- `.github/ISSUE_TEMPLATE/workstream.yml`
- `.github/PULL_REQUEST_TEMPLATE.md`

Validation and synchronization:

- `tools/governance/work_state.py`
- `tests/governance/__init__.py`
- `tests/governance/test_work_state.py`
- `.github/workflows/work-state.yml`

Checkpoint records:

- `docs/work/governance/GWS1/**`

No other path is authorized. In particular, do not edit `src/**`, product tests, fixtures, solution/project/package/SDK
files, external projects, or OpenCode configuration.

## Required record fields

Each record must contain:

- schema version and stable work-item ID;
- source kind, GitHub number/URL when applicable, title, work kind, owner, and track;
- lifecycle state and explicit dependency IDs;
- contract authority URL/revision and frozen baseline when implementation-capable;
- checkpoint ID/path when active or later;
- branch and PR identity when known;
- one `selectedForExecution` flag and a next action when selected;
- expected GitHub issue state and lifecycle label;
- a short status reason that does not become a second state field.

Closed historical records may use their merged/closed issue as contract revision and may omit a checkpoint or baseline
that predates durable checkpoints. Open implementation records fail validation if required readiness fields are absent.

## Migration mapping

- Closed GitHub issues: `Closed`.
- Parent workstreams #3 and #4: `Active` while open children remain.
- Issue #12: `Active`, checkpoint `I12`, activation branch/PR #49.
- Issue #13: `Blocked` by #12.
- Issue #16: `ReviewRequired`, PR #48 with changes requested.
- Issue #17: `Blocked` by #16.
- Issue #18: `Blocked` by #16 and #17.
- Maintainer checkpoint `GWS1`: `Active` and selected for this execution.

Do not infer a lifecycle from arbitrary label combinations after migration. The canonical record drives labels.

## Non-goals

- Changing issue specifications, semantic-delivery contracts, product behavior, or historical completion evidence.
- Replacing GitHub issue bodies as behavior/specification authority.
- Storing raw credentials or GitHub tokens.
- Letting CI silently rewrite pull-request branches.
- Building a general project-management service.
- Committing local OpenCode agent configuration.

## Risk inventory

1. Two records select execution, or execution points to a missing/closed work item.
2. `Ready` or `Active` lacks a contract revision, baseline, owner, target capsule where required, or satisfied dependency.
3. A blocked item is accidentally treated as implementation-authorized.
4. A closed dependency, parent, acceptance item, or synthetic maintainer checkpoint is rejected by one overly rigid shape.
5. Cyclic or missing dependencies pass validation.
6. GitHub issue state/labels drift without a visible failure or deliberate synchronization step.
7. Generated `execution.json` is hand-edited and becomes independent authority again.
8. Narrative status or checkpoint state contradicts the registry and agents trust the wrong file.
9. Parallel active contributors are blocked by one global active item.
10. Migration loses owner, track, issue URL, PR, baseline, or frozen contract evidence.
11. JSON output depends on checkout path, timestamps, dictionary order, or network response order.
12. A workflow gains broader GitHub write permission than lifecycle-label synchronization requires.
13. A fork or offline contributor cannot validate canonical state without GitHub access.

## Existing coverage

- `docs/project/issue-readiness.md` defines frozen readiness and amendment rules.
- `docs/project/workflow.md` defines at most one selected checkpoint per Orchestrator session; zero represents idle.
- `docs/project/delegated-contribution-workflow.md` defines review and repair transitions.
- GitHub labels and dependency links provide the current remote projection.
- `docs/project/execution.json` provides the current local selection shape.
- PR #43 demonstrates that these sources can drift and require manual reconciliation.

No existing automated test validates the complete lifecycle graph or projection drift.

## Test strategy and soft budget

No Test Writer is required because this is mechanical governance tooling, not compiler, IR, persistence, deterministic
product output, or product acceptance behavior. The Builder owns focused validator tests.

Soft budget: 10 to 14 distinct governance tests covering valid migration, schema rejection, legal/illegal transitions,
missing/cyclic dependencies, readiness requirements, parallel active items, at-most-one selected execution item and idle,
deterministic projection, stale execution detection, offline validation, and GitHub-label drift parsing. Do not test every
JSON field independently.

## Focused verification

```powershell
python -B tools/governance/work_state.py validate --root .; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; python -B tools/governance/work_state.py project-execution --root . --check; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; python -B -m unittest tests.governance.test_work_state; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; git diff --check
```

## Review boundary

Implementation stops at `ReviewRequired`. The Orchestrator inspects the actual complete diff, invokes one independent
Reviewer, and records every finding as Fixed, Rejected with evidence, or Deferred with explicit owner approval. Run the
final gate only after all findings are resolved. After two failed repair reruns, set `GWS1` to `Blocked`, preserve the
worktree, and stop.

## Final gate

Run once after review resolution:

```powershell
python -B tools/governance/work_state.py validate --root .; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; python -B tools/governance/work_state.py project-execution --root . --check; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; python -B tools/governance/work_state.py check-github --root . --repository Bilaltariq41/SeqDoc; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; python -B -m unittest tests.governance.test_work_state; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; git diff --check
```

The GitHub check is read-only in the final gate. Label synchronization is a separate explicit maintainer action after the
candidate is accepted.
