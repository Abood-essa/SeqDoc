# CT-0 CreditTransfer Baseline

## Candidate

- External revision: `02b82a5`
- Analysis workspace: detached disposable Git worktree
- Intended profile: Release / `net9.0`
- Current generated output: **withheld**; SeqDoc did not complete activation, so no diagrams were manually fabricated.

The owner checkout was unchanged by admission work. Sanitized initial and final porcelain status both had revision
`02b82a5`, one local commit ahead of its tracked branch, zero tracked changes, six top-level untracked entries, and
Git object digest `e0e1c6c43430a35743c73bceccec3c46baf64089`. Paths and contents are intentionally omitted. Only `.seqdoc/` was
created inside the disposable worktree.

## Solution topology

The solution contains eight projects:

1. ASP.NET Core Web host
2. CoreWCF Services host
3. Business/data Engine library
4. Minimal HTTP host
5. HTTP IVR controller host
6. Windows recovery service
7. Virgin Windows service
8. Test client

The provisional executable-entry denominator includes controller HTTP actions, one Minimal API GET handler, CoreWCF
operations, and two worker/recovery processes. This remains provisional until compiler-backed admission succeeds; it
must not be reported as covered behavior.

## Admission evidence

### Whole solution

Current solution analysis failed closed with `SD1012`: heterogeneous target-framework sets cannot be represented by
one profile set, so SeqDoc requires a root project. No Program Index or documentation was produced.

The whole solution build reached all projects but failed in the two Windows-service projects with 77 warnings and 12
errors. The observed blockers were malformed program scaffolding in one service and unavailable legacy installer
types in the other. These are separate admission partitions; they do not invalidate the successfully buildable Web,
Services, and Engine chain.

### Web → Services → Engine partition

`CreditTransferWeb/CreditTransfer.csproj` and its Services/Engine references built successfully for `net9.0` with 14
package-vulnerability warnings and zero compile errors.

The first Web analysis, before that build established generated assets, failed the compiler gate with 2,612 errors.
The one permitted repair rerun progressed through extraction and then crashed during persistence activation.

Exact regression signature:

```text
System.NotSupportedException: deserialization of SeqDoc.Core.Behavior.LoopNode is not supported
Path: $.MethodFlows[27].Nodes[30]
BehaviorSnapshotJsonCodec.Deserialize, line 25
Stage: staged activation validation
```

This is now the immediate prerequisite to producing current CreditTransfer diagrams. It is a generic persistence
round-trip defect exposed by a realistic natural loop, not a reason to add CreditTransfer-specific behavior.

## Current capability result

| Partition | Current result |
|---|---|
| Whole solution as one profile | Unsupported required feature (`SD1012`) |
| Web/Services/Engine build | Buildable with warnings |
| Web/Services/Engine analysis | Extracted, then failed persistence activation |
| Windows recovery service | Build-blocked source partition |
| Virgin Windows service | Build-blocked legacy installer partition |
| Generated Markdown/Mermaid | None; correctly withheld |

## Next capability order

1. Repair and regression-test polymorphic Method Flow persistence for `LoopNode` while preserving previous-valid-state
   and deterministic activation guarantees.
2. Regenerate the Web partition immediately and inspect the first real diagrams.
3. Continue the approved generic predicate, root, exact-call, and multi-project sequence from measured output.
4. Admit heterogeneous project roots separately until a later generic solution-profile composition capability is
   explicitly designed.

No configuration values, credentials, private source excerpts, or external build output are retained here.
