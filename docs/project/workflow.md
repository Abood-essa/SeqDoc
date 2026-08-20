# Development Workflow

## Session start

1. Read `docs/README.md`, `docs/project/status.md`, and `docs/project/execution.json`.
2. Read `docs/roadmap.md` and only the architecture/decision material relevant to the active checkpoint.
3. Inspect Git status and preserve unrelated work.
4. If execution is idle, do not begin product implementation without owner activation.
5. If execution is active, read the named checkpoint capsule and state immediately before delegation or editing.

Durable repository files are execution authority. Conversation summaries, model memory, and raw session exports are
recovery aids only.

## Checkpoints

Work on one checkpoint at a time under `docs/work/<pass>/<checkpoint>/`. Each checkpoint declares exact target
paths, non-goals, risks, existing coverage, a soft test budget, one focused implementation command, and one final
gate. States are `NotStarted`, `Building`, `ReviewRequired`, `ResolvingFindings`, `Verifying`, `Closed`, or
`Blocked`.

The Orchestrator drafts the capsule and delegates implementation; it does not edit product source, tests, build, or
OpenCode configuration. Implementation stops at `ReviewRequired`. Invoke the independent Reviewer once, inspect the
actual diff, and record every finding as `Fixed`, `Rejected` with evidence, or `Deferred` with explicit owner
approval. Run the final gate only after findings are resolved. After two failed repair reruns, preserve the worktree,
mark the checkpoint `Blocked`, and stop.

## Scope and verification

- Static compiler evidence remains authoritative; incomplete identity fails closed.
- Never broaden a checkpoint to solve adjacent discoveries. Record them for later planning.
- Use risk-based tests at the least expensive reliable layer and avoid duplicate assertions.
- Run focused verification during implementation and the declared final gate once after review resolution.
- Do not repeat a successful command against an unchanged candidate.
- Full-solution and external-project lanes run only when the checkpoint or milestone risk requires them.

## Agent routing

The maintainer may use local orchestration, implementation, test-writing, exploration, and independent-review roles.
Local agent/tool configuration is not repository authority and is never committed. Public contributors follow
`AGENTS.md` and their assigned GitHub issue regardless of the coding tool they use.

## Repository and session safety

The canonical GitHub repository is public. Before committing or pushing, inspect status, diff, recent commits, and
the exact staged paths. Never upload credentials, raw OpenCode session exports, external-project source, build
outputs, or machine-local scratch. Keep restart decisions in `docs/project/` so creating a remote or starting a new
agent session cannot erase execution authority.
