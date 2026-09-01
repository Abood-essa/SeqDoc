# SeqDoc Documentation

This page orients a new development session without relying on conversation history.

## Product authority

- `README.md` describes the product and entry commands.
- `docs/architecture.md` describes the compiler-evidence pipeline.
- `docs/decisions.md` records accepted architectural constraints.
- `docs/roadmap.md` is the ordered capability direction.
- `docs/project/parallel-workstreams.md` defines current contributor-owned workstreams and dependencies.
- `docs/usage.md` contains reproducible CLI and external-corpus setup.

The non-negotiable invariants are static-analysis authority, explicit evidence and certainty, profile and target-
framework separation, deterministic output, typed Program Index → Method Flow → Scenario Graph → Diagram Plan
boundaries, and preservation of the previous valid state after failed analysis.

## Execution authority

`docs/project/work-items/` is the sole typed current-state authority. Read the applicable record before GitHub or any
projection. GitHub labels, `execution.json`, status, parallel topology, and checkpoint state are projections or history.
Use the governance `transition` command for lifecycle changes.

Read the selected record in `docs/project/work-items/`, then `docs/project/status.md`, `docs/project/workflow.md`, and `docs/project/execution.json` at session start.
`execution.json` identifies the only active checkpoint. When it is idle, planning and repository maintenance may
continue, but product implementation waits for owner activation.

Raw agent session exports are local recovery artifacts and are not repository authority. Durable decisions and
the restart position are recorded in `docs/project/` so a fresh session can resume safely.
