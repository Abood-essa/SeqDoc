# PF-1 — Referenced-Project Framework Identity

## Purpose

Prevent a referenced project from borrowing an ancestor project's `project.assets.json` target framework. Program
Index must report the framework actually evaluated for each loaded project, preserving deterministic profile isolation.

## Target paths

- `src/SeqDoc.Analysis.Roslyn/Workspace/CompilationWorkspaceLoader.cs`
- `src/SeqDoc.Analysis.Roslyn/Workspace/LoadedCompilationProfile.cs`
- `src/SeqDoc.Analysis.Roslyn/ProgramIndex/RoslynProgramIndexExtractor.cs`
- `tests/SeqDoc.Analysis.Tests/MultiTargetProgramIndexTests.cs`
- `tests/fixtures/PassA/MultiTargetProfiles/**`
- `docs/work/foundation/PF-1/**`

## Accepted design

Use project-local evaluated/Roslyn/MSBuild evidence for each loaded project's effective target framework. Never search
ancestor directories for another project's assets file. If exact project-local identity is unavailable, degrade
conservatively without labeling a reference with the root profile framework. Preserve compilation profiles, project
references, fingerprints, and deterministic ordering.

## Non-goals

- No external corpus, traversal, framework-model, CLI, or persistence changes.

## Risks and verification

Risks include multi-target reference selection, stale assets, profile leakage, and changed Program Index fingerprints.
Existing MultiTargetProfiles coverage proves source/reference isolation and now reliably reproduces the defect. Add at
most two focused boundary claims for absent local versus ancestor assets.

```powershell
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --filter "FullyQualifiedName~MultiTargetProgramIndexTests"
```

Final gate covers the affected profile/framework boundary without rerunning every expensive Roslyn fixture:

```powershell
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~MultiTargetProgramIndexTests|FullyQualifiedName~CompilationProfileResolverTests|FullyQualifiedName~CompilationProfileAnalyzerTests"
```
