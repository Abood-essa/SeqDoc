# CT-1 — LoopNode Persistence Prerequisite

## Purpose

Repair the generic `LoopNode` behavior-snapshot persistence defect exposed by the buildable CreditTransfer Web
partition. A behavior snapshot containing a natural loop must serialize, stage, deserialize, validate, and activate
without losing canonical loop membership or changing existing persistence guarantees.

## Target paths

- `src/SeqDoc.Core/Behavior/MethodFlowContracts.cs`
- `tests/SeqDoc.Persistence.Tests/SqliteAnalysisStoreTests.cs`
- `docs/work/credit-transfer/CT-1/**`

## Accepted design

Prefer the smallest compatible constructor-selection repair: make the complete existing `LoopNode` constructor the
unambiguous JSON constructor so all serialized properties, including `BodyBlockOrdinals`, participate in round-trip
construction and existing canonicalization. Do not add a persistence-only duplicate representation unless evidence
shows the constructor annotation is insufficient.

## Non-goals

- No persistence schema or payload-version change.
- No Method Flow, loop detection, identity, fingerprint, Scenario, wording, or rendering semantics.
- No CreditTransfer-specific rule or external source edit.
- No broad converter rewrite or changes to other polymorphic node types without a reproduced defect.
- No retry of CreditTransfer generation until the checkpoint final gate passes.

## Risk inventory

1. System.Text.Json continues to see two eligible constructors and fails at activation.
2. `BodyBlockOrdinals` is omitted, reordered, or bypasses nonnegative/distinct/stable canonicalization.
3. The repair changes serialized bytes or fingerprints for snapshots that do not contain loops.
4. Staged activation succeeds but the active snapshot reconstructs a different `LoopNode` shape.
5. A failure path weakens atomic activation or previous-valid-state preservation.

## Existing relevant coverage

- Polymorphic `FlowNode` registration already includes `LoopNode`.
- Method Flow and Scenario tests cover natural-loop construction and canonical body block ordinals.
- Persistence aggregate round-trip tests currently cover only entry/exit nodes.
- Atomic, failed, and cancelled aggregate activation tests cover previous-valid-state behavior but not a loop payload.

## Soft test budget

At most three distinct claims in one focused persistence test: activation succeeds, the active node is a `LoopNode`,
and its complete canonical fields/serialized snapshot round-trip unchanged. Do not duplicate loop-builder tests.

## Focused verification command

```powershell
dotnet test tests/SeqDoc.Persistence.Tests/SeqDoc.Persistence.Tests.csproj -c Release --filter "FullyQualifiedName~LoopNode"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Persistence.Tests/SeqDoc.Persistence.Tests.csproj -c Release
```
