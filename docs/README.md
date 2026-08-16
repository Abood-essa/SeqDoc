# SeqDoc Documentation

This page orients a new development session without relying on conversation history.

## Product authority

- `README.md` describes the product and entry commands.
- `docs/architecture.md` describes the compiler-evidence pipeline.
- `docs/decisions.md` records accepted architectural constraints.
- `docs/roadmap.md` is the ordered capability direction.

The non-negotiable invariants are static-analysis authority, explicit evidence and certainty, profile and target-
framework separation, deterministic output, typed Program Index → Method Flow → Scenario Graph → Diagram Plan
boundaries, and preservation of the previous valid state after failed analysis.

## Execution authority

Read `docs/project/status.md`, `docs/project/workflow.md`, and `docs/project/execution.json` at session start.
`execution.json` identifies the only active checkpoint. When it is idle, planning and repository maintenance may
continue, but product implementation waits for owner activation.

Raw OpenCode session exports are local recovery artifacts and are not repository authority. Durable decisions and
the restart position are recorded in `docs/project/` so a fresh session can resume safely.
