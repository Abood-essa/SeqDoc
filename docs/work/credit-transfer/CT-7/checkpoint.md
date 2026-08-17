# CT-7 — Diagram-First Markdown

## Purpose

Make the sequence diagram the first substantive section of every generated Markdown document so reviewers see the
primary artifact before detailed behavior and diagnostics. This is a mechanical renderer-ordering repair isolated from
the larger presentation semantics exposed by CreditTransfer regeneration.

## Target paths

- `src/SeqDoc.Rendering.Markdown/MarkdownRenderer.cs`
- `tests/SeqDoc.Rendering.Tests/MarkdownRendererTests.cs`
- `docs/work/credit-transfer/CT-7/**`

## Accepted design

1. Preserve the document title and evidence disclaimer first.
2. Emit `## Sequence diagram` and the complete Mermaid fence immediately after the disclaimer.
3. Emit `## Behavior` after the Mermaid fence, followed by `## Technical fallback` when present.
4. Preserve every phrase, evidence/certainty annotation, Mermaid byte, heading text, canonical newline, and index
   document exactly; only section order changes.

## Non-goals

- No participant naming, line wrapping, caller removal, logging filtering, argument presentation, placeholder-label
  removal, topology, Diagram Plan, Mermaid, wording, or index behavior changes.

## Risk inventory

1. Behavior or fallback phrases could be omitted or duplicated during reordering.
2. Mermaid fences/newlines could become invalid.
3. Index rendering or Mermaid content could change accidentally.

## Existing relevant coverage

- Acceptance tests assert generated Markdown contains behavior, technical fallback, and sequence sections.
- Mermaid renderer and validator tests cover diagram bytes and syntax independently.
- No focused test currently pins Markdown section order.

## Soft test budget

At most two claims: exact section ordering with fallback present, and preservation/order when fallback is absent. One
test may also prove the Mermaid payload remains unchanged.

## Focused verification command

```powershell
dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --filter "FullyQualifiedName~MarkdownRenderer"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release
```
