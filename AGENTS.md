# SeqDoc Contributor Agent Guide

SeqDoc is a .NET static-analysis CLI that produces evidence-backed Markdown and Mermaid. This file is the canonical
project instruction source for coding agents. Human contributors follow the same engineering and review standard.

## Start every task

1. Read the assigned GitHub issue completely, including dependencies and acceptance criteria.
2. Read `README.md`, `docs/architecture.md`, `docs/decisions.md`, `docs/contributing.md`, and
   `docs/project/testing-policy.md`.
3. Inspect `git status`, the target files, nearby tests, and recent commits before proposing changes.
4. Comment a short implementation plan on the issue or draft PR. Identify target paths, risks, tests, and blockers.
5. Stay inside the issue scope. Ask before changing architecture, public contracts, or unrelated files.

GitHub issues are execution authority for contributor work. Files under `docs/project/` are maintainer-owned durable
strategy and execution state; do not edit them unless the issue explicitly requires it.

## Product invariants

- Static/compiler evidence is authoritative. Never invent exact behavior when evidence is incomplete.
- Keep compilation profiles and target frameworks separate.
- Preserve the typed pipeline: Program Index, Method Flow, Scenario Graph, Diagram Plan.
- Every user-facing fact retains evidence and certainty.
- Failed analysis preserves the previous valid state.
- Output must not depend on checkout path, scheduling, timestamps, or unstable iteration.
- Keep `SeqDoc.Core` free of Roslyn, MSBuild, SQLite, CLI, and renderer dependencies.
- Propagate cancellation through long-running operations.
- Never use application, route, type, method, or business names as production matching rules.

## Implementation workflow

1. Reproduce the problem or establish a focused red test before changing behavior.
2. Prefer the smallest generic contract that solves the issue. Do not implement later roadmap stages incidentally.
3. Reuse existing typed facts and helpers; do not rescan source in application or rendering layers.
4. Preserve stable identities, canonical ordering, evidence, certainty, and backward-compatible defaults.
5. Add risk-based tests at the least expensive reliable layer. Avoid duplicate assertions across layers.
6. Run focused tests during implementation. Run the issue's final gate once after self-review.
7. Inspect the complete diff, not only files you remember changing.

When blocked, stop and report the exact command, error, evidence, and smallest decision needed. Do not weaken tests,
remove conservative diagnostics, guess semantics, or expand scope to make the task appear complete.

## Self-review before opening a PR

- Re-read the issue and verify every acceptance criterion.
- Check `git diff --check`, `git status`, and the full diff from `main`.
- Look for false positives, profile leakage, unstable ordering, missing evidence/certainty, and previous-state regressions.
- Confirm negative and boundary cases, not only the happy path.
- Remove debug output, generated files, secrets, local paths, copied external source, and unrelated refactoring.
- Run the focused command and declared final gate; record exact counts and any unavailable external lanes.
- For generated diagrams, inspect the actual Markdown/Mermaid and use Mermaid CLI when layout behavior changed.

## External test projects

Supplied and open-source applications live in sibling `../SeqDoc-TestProjects`, or the directory named by
`SEQDOC_TEST_PROJECTS_ROOT`. Never commit their source, configuration, credentials, caches, build output, or generated
documentation to SeqDoc. See `docs/usage.md` for setup.

## Pull requests and review

- Work in a fork and a focused branch. Never push directly to SeqDoc `main`.
- Link the assigned issue with `Closes #<number>` and list included sub-issues.
- Describe the problem, design, risks, changed paths, focused verification, final gate, and remaining boundaries.
- Open a draft PR early for substantial work, but request review only after tests and self-review pass.
- The maintainer will batch findings. Fix every finding on the same PR branch, explain the repair, rerun only affected
  focused tests plus the required gate, and request review again.
- Do not rewrite canonical roadmap/status files to claim completion. The maintainer updates them after merge.

The repository is licensed under MPL-2.0. By contributing, you agree to the terms in `docs/contributing.md`.
