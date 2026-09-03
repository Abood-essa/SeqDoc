# DGP1 independent review and repair rerun 1

Review batch: one independent review. Repair rerun 1 was incomplete for F3. Repair rerun 2 is the last permitted repair.

| Finding | Disposition | Exact repair path(s) | Evidence |
|---|---|---|---|
| DGP1-F1 | Fixed | `.opencode/agents/github-observer.md` | Broad `github_*` is denied before read-shaped allow patterns; all non-GitHub tools remain denied and the filtered host is documented as a second boundary. |
| DGP1-F2 | Fixed | `docs/project/parallel-workstreams.md`, `README.md`, `docs/README.md` | Historical topology is dated and no longer declares current owners, plans, ladders, or mutable dependencies; navigation labels it historical and points to canonical strategy/registry. |
| DGP1-F3 | Fixed in rerun 2 | `docs/project/workflow.md`, `docs/project/collaboration-model.md`, `AGENTS.md`, `docs/project/issue-readiness.md`, `docs/project/delegated-contribution-workflow.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `.opencode/agents/orchestrator.md` | Workflow lines 28-32 now point to centralized epochs and explicitly grandfather DGP1/I13/P17-R1/QHTTP-B under one review; new work uses pre-Ready, ReviewRequired, and post-repair epochs, with two failed rounds Blocked and preserved. Rerun 1 was incomplete because workflow retained a global one-review instruction. |
| DGP1-F4 | Fixed | `docs/project/completion-roadmap-visual.md` | Visual now states Blocked/preserved after two failed rounds and permits only a separately authorized new decision. |
| DGP1-F5 | Fixed | `.github/ISSUE_TEMPLATE/implementation.yml`, `.github/ISSUE_TEMPLATE/workstream.yml` | Templates request planning-time IDs, baseline/planned branch, tier, proposed lease, observable, focused command, and final gate; candidate heads and receipts remain in PR evidence. |
| DGP1-F6 | Fixed | `docs/project/collaborator-setup.md` | Workflow link is `../../.github/workflows/work-state.yml`. |

Residual: byte-for-byte comparison with deleted untracked drafts is unavailable because those drafts were not in baseline
Git. The canonical issue map retains all known IDs, dependencies, owner distinctions, boundaries, creation rules, and
retirement rules without claiming byte identity. `github-observer.md` changed for F1; both agent files remain locally
ignored. Fixture lock files remain untouched. The current session MCP startup failure requires a local permission/config
fix, OpenCode restart, and read-only smoke test; it is not a publication configuration.

## Post-review verification note

The first final-gate attempt was unavailable before execution because the public Gate Runner policy omitted the narrow
`python -B -m unittest tests.governance.test_work_state*` permission. That permission was added narrowly; no test result
or candidate status change came from the rejected attempt. The corrected exact command passed exit 0 with 32 valid work
items, current execution projection, `git diff --check` passed, and a complete, non-truncated result. Gate Runner duration
and SHA/status envelope were unavailable because exact-command restrictions forbade extra commands; Orchestrator pre-gate
inspection recorded unchanged status and baseline HEAD `08cb735945a178e93458069d6c42da833e044a74`. Focused repair evidence
remains 15 governance tests passed. There was one independent review only; F1-F6 were all fixed and no second review
occurred.
