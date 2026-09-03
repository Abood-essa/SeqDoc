# QFIX1 package lock publication checkpoint

## State

`Closed`

This synthetic checkpoint is local and has no GitHub issue or branch. Owner authorization permits briefly deselecting
I13, preserving every GH-13 field except `selectedForExecution`, and reselecting I13 after QFIX1 closes.

## Objective

Publish the two missing package lock files generated while preparing the CLI suite. Repository-wide
`RestorePackagesWithLockFile` is true; their absence caused clean fixture preparation failure. Prove both package-free
fixtures restore in locked mode and their existing CLI scenarios pass. Make no semantic or product behavior changes.

## Target paths

- `tests/fixtures/PassA/DuplicateConstructedInterfaces/packages.lock.json`
- `tests/fixtures/PassA/ManyDiagnostics/packages.lock.json`
- `docs/project/work-items/QFIX1.json`
- `docs/project/work-items/GH-13.json` only for selection
- `docs/project/execution.json`
- `docs/work/quality/QFIX1/**`

## Non-goals

No fixture project or source edits, product source, tests, build/package/SDK policy, solution changes, other lock files,
DGP1 changes, external projects, GitHub or worktree operations.

## Risk inventory and existing coverage

- Empty package locks are mistaken for disposable noise even though inherited lock policy requires them.
- Locks were generated under the wrong target framework or restore mode.
- Locked restore passes while the exact consuming CLI tests still fail.
- Restores alter tracked or unrelated files.
- I13 lifecycle, contract, baseline, branch, or next action changes while deselected.

`CliProcessTests.AnalyzePersistsCollapsedConstructedInterfaceEdges` consumes `DuplicateConstructedInterfaces`.
`CliProcessTests.BuildFailureBoundsPrimaryDiagnosticsAndRetainsCompleteArtifact` consumes `ManyDiagnostics`.
Both fixtures target `net10.0`, have no package dependency entries, and require lock version 2 with an empty `net10.0`
dependency object. No new tests are needed.

## Soft test budget

Zero new tests. Existing exact CLI tests cover consumption.

## Focused verification command

```powershell
dotnet restore tests/fixtures/PassA/DuplicateConstructedInterfaces/DuplicateConstructedInterfaces.csproj --locked-mode; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet restore tests/fixtures/PassA/ManyDiagnostics/ManyDiagnostics.csproj --locked-mode; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~AnalyzePersistsCollapsedConstructedInterfaceEdges|FullyQualifiedName~BuildFailureBoundsPrimaryDiagnosticsAndRetainsCompleteArtifact"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; git diff --check
```

## Review boundary

Inspect exact lock content and status before verification. Run the focused command once after self-review, then stop at
`ReviewRequired`. Run the final gate only after review resolution.

## Final gate

```powershell
git status --short --untracked-files=all; dotnet restore tests/fixtures/PassA/DuplicateConstructedInterfaces/DuplicateConstructedInterfaces.csproj --locked-mode; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet restore tests/fixtures/PassA/ManyDiagnostics/ManyDiagnostics.csproj --locked-mode; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; git status --short --untracked-files=all; git diff --check
```

The Orchestrator must compare the two complete status sequences for exact equality. Any added, modified, or deleted
path caused during either restore fails the gate. This remains valid despite pre-existing DGP1 changes because equality,
not cleanliness, is the invariant.

## Final verification evidence

The exact pre-restore and post-restore full status sequences were identical; the output was complete and not truncated.
Both locked restores passed and reported up to date. `git diff --check` passed. The exact final command exited 0.
The focused CLI consumers passed 2/2. Candidate SHA and duration are unavailable because the exact final command did not
query or report them. Baseline working HEAD remains `08cb735945a178e93458069d6c42da833e044a74` from Orchestrator
inspection. GH-13 preservation was inspected against its baseline diff; only `selectedForExecution` changed.
