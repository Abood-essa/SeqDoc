# CT-11 — Mermaid Layout Integrity

## Purpose

Repair the rendered-layout regression exposed by the final CreditTransfer preview: filtered terminal messages left
empty nested control fragments that collapse over participant headers, and quoted aliases display literal quotes in
the VS Code Mermaid renderer.

## Target paths

- `src/SeqDoc.Rendering.Markdown/MermaidRenderer.cs`
- `src/SeqDoc.Rendering.Markdown/MermaidValidator.cs`
- `tests/SeqDoc.Rendering.Tests/**`
- `docs/work/credit-transfer/CT-11/**`

## Accepted design

1. Recursively omit fragments and arms with no surviving message or child-fragment content; never emit an empty
   `break`, `alt`, or `opt` container.
2. Preserve non-empty fragment order and labels exactly. Unsupported empty termination remains technical fallback and
   never becomes an invented message.
3. Emit participant aliases without visible quote characters while preserving deterministic Mermaid-safe escaping.
4. Pin the exact collapsed-fragment regression and execute an actual Mermaid parse/render smoke check before closure.

## Focused command and gate

```powershell
dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release
```
