# WindowsDependency lock repair checkpoint

## State

`Closed`

Owner activation was granted at https://github.com/Bilaltariq41/SeqDoc/issues/58#issuecomment-5508271854 from
`origin/main` at `c6216c25c449b95ac4bb7fce17eff1d37d97c3da`. Issue #58 is the frozen mechanical contract.
The failed one-file attempt proved that NuGet regeneration removes synthetic lock identities required by PF-1. Owner
amendment https://github.com/Bilaltariq41/SeqDoc/issues/58#issuecomment-5508381827 therefore authorizes one test path
solely to install and restore synthetic lock input around the existing fallback assertion.
The one independent review produced WDL1-F1 and WDL1-F2. Owner amendment
https://github.com/Bilaltariq41/SeqDoc/issues/58#issuecomment-5508469494 authorizes the governance fixture test only to
remove copied live selections before synthetic scenarios and requires exact no-BOM synthetic lock bytes.

## Objective

Regenerate the WindowsDependency fixture lock so a clean standalone `--locked-mode` restore succeeds while preserving
the existing multi-target referenced-project framework and lock-fallback assertions unchanged. Merge the isolated
repair and authorize Abood to apply only its exact merge SHA to P17-R1 before resuming unfinished verification lanes.

## Target paths

- `tests/fixtures/PassA/MultiTargetProfiles/References/WindowsDependency/packages.lock.json`
- `tests/SeqDoc.Analysis.Tests/MultiTargetProgramIndexTests.cs`
- `tests/governance/test_work_state.py` only for WDL1-F1's synthetic-fixture selection normalization
- `docs/project/work-items/GH-58.json`
- `docs/project/work-items/GH-13.json` only for local Orchestrator selection transfer; no I13 lifecycle or contract edit
- `docs/project/execution.json`
- `docs/work/governance/WDL1/**`

## Non-goals

- No project, source, product, unrelated test, package-version, SDK, build, workflow, P17-R1, or I13 behavior change.
- No `--force-evaluate` in verification, stale `obj` reliance, skipped lane, or weakened assertion.
- No redesign of the PF-1 synthetic lock fallback in this repair.

## Risk inventory

1. Normal regeneration removes the synthetic portable/Windows identities consumed by existing tests.
2. A hand-edited lock is not reproducible and only appears valid.
3. Stale `obj` output hides the locked-restore mismatch.
4. Portable and Windows package/framework identity crosses profiles.
5. Scope expands to the project or tests to make the gate pass.

The generated lock must remain ordinary NuGet output. Synthetic framework/package identities may exist only as
test-scoped input installed and restored by the one amended test path. Do not broaden scope further.

## Existing relevant coverage

`MultiTargetProgramIndexTests` covers portable/Windows source and reference isolation, exact evaluated referenced-project
framework, local-lock fallback without assets, and build-order determinism. It remains unchanged.

## Soft test budget

Zero new tests. Preserve every existing assertion; the sole test edit may isolate synthetic lock data from the real
generated lock. This remains a mechanical repair, so no Test Writer is justified.

## Focused verification command

```powershell
Remove-Item -Recurse -Force "tests/fixtures/PassA/MultiTargetProfiles/References/WindowsDependency/obj" -ErrorAction SilentlyContinue; dotnet restore tests/fixtures/PassA/MultiTargetProfiles/References/WindowsDependency/WindowsDependency.csproj --locked-mode; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --filter "FullyQualifiedName~MultiTargetProgramIndexTests"
```

## Review boundary

Stop at `ReviewRequired` after focused verification. The Orchestrator inspects the generated lock diff and invokes one
independent mechanical Reviewer. Record every disposition. Run the final gate only after findings are resolved.

## Independent review dispositions

One independent review ran; no second review will run.

- **WDL1-F1 — Fixed.** The copied governance fixture now clears every live selection before constructing synthetic
  transition scenarios. The real WDL1 and I13 records are untouched. Governance tests pass 15/15.
- **WDL1-F2 — Fixed.** The fallback test installs exact no-BOM, LF-terminated bytes matching the prior synthetic lock
  and restores the generated lock byte-for-byte in `finally`. Clean locked restore passes and
  `MultiTargetProgramIndexTests` pass 7/7.

## Final gate

```powershell
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release
```

Also run `git diff --check` and verify no path beyond the amended seven-path checkpoint allowlist changed. Record SDK/NuGet
identity, exact counts, any baseline failure signature, and the merge SHA.

The complete Analysis gate ran after review resolution. Candidate and untouched `c6216c25` baseline each reported
205 passed, 69 failed, and 274 total. All 69 failed test names were identical; their signatures are unavailable
MediatR, EF, CoreWCF, and FusionCache fixture dependencies plus the absent external corpus. No WDL1 regression was
present. The first gate process exceeded the runner's 120-second operational limit; the exact unchanged command was
rerun with a sufficient timeout and completed in 8 minutes 2 seconds. The baseline comparison completed in 8 minutes
20 seconds.

PR #60 merged as `1fcbe0dbde0bfc59a71fdf9fab2ff62d6a9f0bf2`. The merged fixture repair is the only
revision authorized for P17-R1 application. WDL1 is closed and local Orchestrator selection returns to active I13.
