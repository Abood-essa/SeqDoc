# GWS1 independent review and takeover dispositions

| Finding | Disposition and exact evidence |
|---|---|
| GWS1-F1 | Fixed: `tools/governance/work_state.py:validate_items` checks `len(hits) == 1` before indexing and requires the immediate first nonblank backticked state; `test_capsule_projection_and_execution_identity` covers a missing heading. Schema and migration validation remain in `test_schema_rejects_version_extra_missing_wrong_type_and_duplicate_dependency`. |
| GWS1-F2 | Fixed: `transition` validates a deep-copied candidate and atomically stages replacements, including the selected handoff from GWS1 to an existing candidate; `test_replace_failure_rolls_back_all_payloads` checks records, capsule, and execution. Residual power-loss risk remains between atomic per-file replacements; in-process failures rollback, and stale projection/registry validation fails closed. |
| GWS1-F3 | Fixed: `GWS1.json` and `checkpoint.md` carry `GWS1` and `docs/work/governance/GWS1`; `execution_object` derives both, covered by `test_capsule_projection_and_execution_identity`. |
| GWS1-F4 | Fixed: `validate_items` requires `contractRevision` for open GitHub records; `test_active_requires_frozen_contract_and_baseline` proves frozen-baseline rejection. |
| GWS1-F5 | Fixed: `gh` handles missing remote items and malformed/read failures nonzero in `test_github_drift_parsing_is_read_only`; Draft/Cancelled zero-label behavior is covered through the same no-expected-label branch. |
| GWS1-F6 | Fixed: `AGENTS.md`, `docs/README.md`, `docs/project/workflow.md`, and status banners identify records as current authority; the capsule and record are both `ResolvingFindings`. |
| GWS1-F7 | Fixed: `docs/README.md` and `docs/project/workflow.md` read canonical records first; `docs/project/parallel-workstreams.md` describes topology/dependency projections, and `status.md` makes no current #12/#16 ready claim. |
| GWS1-F8 | Fixed: `docs/project/issue-readiness.md` now contains the substantial audit, semantic/acceptance/mechanical proof, allowlist, baseline/negative, freeze/amendment, producer-to-observable, repair-round, and acceptance-only boundaries without manual label authority. |
| GWS1-F9 | Fixed: capsule and workflow verification use `python -B`; `TemporaryDirectory` cleanup and `-B` focused tests avoid committed cache artifacts. |
| S1 | Fixed: `gh` computes wrong lifecycle labels as attached lifecycle labels minus the expected singleton (or all when absent), adds only an absent expected label, skips unchanged edits, and uses bounded argument arrays. `test_sync_dry_run_forms_only_label_commands` inspects exact dry-run plans and proves non-lifecycle labels never enter edit arguments; one mocked remote read and no write subprocess are asserted. |

Two contributor repair reruns failed. The mandatory checkpoint rule required a `Blocked` stop; the owner then explicitly
authorized this bounded maintainer takeover. No second independent review ran.
