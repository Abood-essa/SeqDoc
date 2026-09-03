# DGP1 documentation and governance publication checkpoint

## State

`Closed`

Baseline: `08cb735945a178e93458069d6c42da833e044a74`.

This synthetic checkpoint is local and has no GitHub issue or branch. The owner authorized pausing local Orchestrator
selection of I13 and will reselect I13 after this publication. GH-13 remains `Active`; its frozen baseline, contract,
branch, and next action are preserved, and only `selectedForExecution` is changed.

## Objective and completion criteria

Publish one navigable documentation and collaboration system: canonical staged roadmap and plain-English visual;
portable read-only gate-runner and GitHub-observer agents; consolidated collaboration/review governance; measured test
performance guidance; updated navigation and templates; and removal of drafts whose meaning now has a canonical home.

Completion means every live meaning has one canonical home, dated snapshots are labelled as evidence, assignments remain
Ahmad P-1..P-5, Qais S-1..S-4, and Abood C-1..C-6, all other future work is unassigned, and no historical `docs/work`
record is rewritten.

## Target paths

Only the user-declared allowlist may change: root and documentation navigation/policy files, `.opencode/.gitignore`,
`docs/project/drafts/**`, `docs/project/completion-issue-map.md`,
the new canonical strategy/governance/runbook/performance files, DGP1 and GH-13 work-item records, execution projection,
the new DGP1 capsule, GitHub templates, and `.opencode/agents/*.md` or `.opencode/commands/*.md`.

## Non-goals

No product source, tests, fixtures, build/package/SDK/workflow changes, external corpus changes, GitHub mutation,
commits, pushes, issues, comments, settings, invitations, secrets, raw session exports, or worktree deletion. Do not
change GH-13 lifecycle, baseline, contract revision, branch, or next action.

## Risk inventory and existing coverage

Risks are competing authority, stale snapshot language, accidental assignment, contradictory review budgets, leaked
local/write assumptions, broken relative links, lost provenance, GH-13 contract drift, excessive collaborator rights,
and test guidance that weakens assertions or promises unmeasured speedups. Existing coverage is
`tools/governance/work_state.py` validation and `tests/governance/test_work_state.py`; GWS1 is historical governance
evidence. No new tests are permitted.

## Test budget and verification

Soft budget: zero new tests. No exception. The focused repair command passed after the final repair. The first final-gate
attempt was unavailable and rejected before execution because Gate Runner lacked permission for the governance unittest
command; it produced no test result and did not change status. The corrected executable final gate for this unchanged
candidate is:

```powershell
python -B tools/governance/work_state.py validate --root .; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; python -B tools/governance/work_state.py project-execution --root . --check; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; git diff --check
```

Focused verification after repair rerun 2 passed: registry validation reported 32 work items, execution projection was
current, `tests.governance.test_work_state` ran 15 tests with `OK`, and `git diff --check` passed. The full focused
command, including those 15 tests, already passed after the final repair. This correction preserves coverage because
repository policy forbids repeating a successful command against an otherwise unchanged candidate. The corrected exact
command passed with exit 0: 32 valid work items, current execution projection, and `git diff --check`
passed; the result was complete and not truncated. Gate Runner's duration and SHA/status envelope were unavailable
because exact-command restrictions forbade extra commands. Orchestrator pre-gate inspection recorded unchanged status
and baseline HEAD `08cb735945a178e93458069d6c42da833e044a74`. The first final-gate attempt was rejected before execution
by tool policy and produced no candidate result. Focused repair evidence remains 15 governance tests passed.

## Draft disposition

- `drafts/completion-issue-map.md` -> `completion-issue-map.md` (canonical future plan; recovered from the available local draft, but byte identity is unavailable because the draft was untracked).
- `drafts/completion-roadmap.md` -> `completion-roadmap.md` (canonical strategy).
- `drafts/completion-roadmap-visual.md` -> `completion-roadmap-visual.md` (canonical ASCII companion).
- `drafts/collaborator-operating-model.md` + `drafts/maintainer-review-policy.md` -> `collaboration-model.md` (merged policy).
- `drafts/collaborator-setup-runbook.md` -> `collaborator-setup.md` (owner/admin branch).
- `drafts/github-collaboration-research.md` -> removed after its adopted facts were represented in collaboration-model
  and collaborator-setup; its dated volatile audit is not current authority.

The implementation and workstream templates now request planning IDs, decision tier, proposed exact lease, baseline and
planned branch, first observable, focused command, and final gate. Candidate heads and Reviewer receipts live in the PR.
DGP1 and the grandfathered I13/P17-R1/QHTTP-B work
remain under the frozen one-review rule; the three-epoch policy starts only when the publication PR merges.
The exact agent files are intentionally hidden by the machine-local `.git/info/exclude` rule. They remain untracked and
must be force-added only at the owner-authorized commit step; no staging occurs in DGP1. `.opencode/.gitignore` remains
at baseline and is not a DGP1 changed path.

## Review boundary and next action

Inspect the complete local diff, account for every moved/deleted draft meaning, and scan for absolute machine paths and
secrets. The two pre-existing untracked fixture lock files remain outside DGP1 and were not touched; fixture
infrastructure needs a separate decision. The current session's GitHub Observer MCP startup failed; resolving local
permission/config conflicts requires a machine-local fix, OpenCode restart, and read-only smoke test. Public config
contains no credential or server setup. DGP1 remains under its one-review rule and is now `Closed`; the corrected gate
passed and the owner-authorized publication batch is next.
