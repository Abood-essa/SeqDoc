# IRP1 completion-program issue registration

## State

`Closed`

## Objective

Register the owner-created completion-program parent and twelve planning-only audit/readiness issues in the canonical
work-item registry, and cross-reference their exact GitHub URLs from the completion issue map. No issue is Ready or
Active, and no product implementation is authorized.

## Target paths

- `docs/project/work-items/GH-69.json` through `GH-81.json`
- `docs/project/completion-issue-map.md`
- `docs/project/completion-roadmap.md`
- `docs/project/work-items/IRP1.json`
- `docs/work/governance/IRP1/**`
- `docs/project/work-items/GH-13.json` selection only
- `docs/project/execution.json`

## Non-goals

- No downstream issue creation, issue mutation, readiness, assignment, or product implementation.
- No roadmap strategy changes beyond the declared map cross-references.
- No current-frontier changes or historical rewrites.

## Risk inventory

- Wrong GitHub number, title, owner, or track.
- Lost dependency edges or incomplete registry projection.
- Planning records interpreted as implementation authority.
- Selected-record collision or unintended I13 mutation.
- Local planning IDs replaced instead of retained in the map.

## Pre-review self-review corrections

- Removed native parent membership from dependency arrays: GH-76 retains GH-13/GH-17/GH-18/GH-53, GH-77 retains
  GH-13, and GH-78 retains GH-17/GH-18.
- Preserved the assignment boundary by marking GH-70/GH-71/GH-72/GH-73/GH-74/GH-78/GH-79/GH-80/GH-81 unassigned;
  firm assignments remain GH-75 to abood, GH-76 to qais, and GH-77 to ahmad.
- Updated affected status reasons to describe unassigned planning/readiness work without implying assignment.

## Independent review and repair

One independent review found IRP1-F1: the completion map and roadmap retained stale authority language for firm
assignments after their registry records were created. Fixed in repair rerun 1 by the smallest bounded T2
publication-consistency amendment to `docs/project/completion-issue-map.md` and
`docs/project/completion-roadmap.md`. No second review occurred.

## Existing coverage

The work-state validator and existing governance unit tests cover schema, blocked/parent records, selection, dependency,
and execution projection behavior. No new tests are needed for this mechanical registry projection.

## Test strategy and soft budget

Zero new tests. Reuse the focused validator, execution projection check, governance unit tests, and diff check.

## Focused verification

```powershell
python -B tools/governance/work_state.py validate --root .; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; python -B tools/governance/work_state.py project-execution --root . --check; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; python -B -m unittest tests.governance.test_work_state; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; git diff --check
```

## Review boundary

Implementation stops at `ReviewRequired`. Inspect the complete target diff before the final gate.

## Final verification evidence

The exact final gate passed with exit 0: 47 work items were valid, the execution projection was current, and
`git diff --check` passed silently. The result was complete and not truncated. The baseline committed candidate at the
gate was `89d8939` from the Orchestrator. SHA, status, and duration were unavailable because the command did not query
them. Focused governance tests passed `15/15`. One independent review occurred; IRP1-F1 was fixed in repair rerun 1,
and no second review occurred.

## Final gate

```powershell
python -B tools/governance/work_state.py validate --root .; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; python -B tools/governance/work_state.py project-execution --root . --check; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; git diff --check
```
