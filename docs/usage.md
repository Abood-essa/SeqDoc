# Using SeqDoc

## Prerequisites

- Windows with the .NET SDK selected by `global.json` (`dotnet --version` verifies it).
- A buildable project or solution to analyze.
- For the supplied acceptance applications, a sibling directory named `SeqDoc-TestProjects`:

```text
Parent/
├── SeqDoc/
└── SeqDoc-TestProjects/
    ├── Provided/
    └── OpenSource/
```

Set `SEQDOC_TEST_PROJECTS_ROOT` to use another location. SeqDoc never requires supplied source to be copied into its
public repository.

## Build the CLI

Run commands from the SeqDoc repository root:

```powershell
dotnet restore SeqDoc.slnx
dotnet build src/SeqDoc.Cli/SeqDoc.Cli.csproj -c Release
```

## Analyze a project or solution

```powershell
dotnet run --project src/SeqDoc.Cli --configuration Release --no-build -- analyze `
  "<project-or-solution>" `
  --repository-root "<source-repository-root>" `
  --configuration Release `
  --framework "<target-framework>" `
  --cache ".seqdoc/cache-v1.db" `
  --output ".seqdoc/output"
```

Important arguments:

| Argument | Purpose |
|---|---|
| target after `analyze` | `.csproj`, `.sln`, or `.slnx` to load |
| `--repository-root` | Stable source root used for path-independent identities |
| `--configuration` | MSBuild configuration, normally `Release` or `Debug` |
| `--framework` | One exact target framework such as `net9.0` |
| `--config` | Optional SeqDoc YAML for exact roots and presentation exclusions |
| `--cache` | SQLite analysis state; keep it outside source or under ignored `.seqdoc/` |
| `--output` | Generated Markdown, Mermaid, index, and diagnostic artifacts |

Use `--all-frameworks` only when no configured exact roots are supplied. SeqDoc keeps profiles separate rather than
guessing which framework owns a configured method.

## Catalog exact method roots

Framework entry points are discovered automatically. To document an exact service, engine, worker, or diagnostic
method, first catalog stable `MethodId` values:

```powershell
dotnet run --project src/SeqDoc.Cli --configuration Release --no-build -- catalog `
  "<project-or-solution>" `
  --kind method `
  --repository-root "<source-repository-root>" `
  --configuration Release `
  --framework "<target-framework>" `
  --cache ".seqdoc/cache-v1.db"
```

Copy complete `method:v1:...` values into `selection.roots`. SeqDoc intentionally does not select roots by short name,
signature substring, or fuzzy matching.

## Reproduce the CreditTransfer hardened suite

The checked-in example configuration contains the six exact configured roots and custom logger exclusion used for the
accepted suite. From the SeqDoc root, with `SeqDoc-TestProjects` beside it, run:

```powershell
dotnet build src/SeqDoc.Cli/SeqDoc.Cli.csproj -c Release

$seqDocRoot = (Get-Location).Path
$testProjectsRoot = if ([string]::IsNullOrWhiteSpace($env:SEQDOC_TEST_PROJECTS_ROOT)) {
  (Resolve-Path "../SeqDoc-TestProjects").Path
} else {
  [System.IO.Path]::GetFullPath($env:SEQDOC_TEST_PROJECTS_ROOT, $seqDocRoot)
}
$creditTransferRoot = Join-Path $testProjectsRoot "Provided/CreditTransfer-om"
$creditTransferProject = Join-Path $creditTransferRoot "CreditTransferWeb/CreditTransfer.csproj"
$seqDocConfig = Join-Path $seqDocRoot "docs/examples/credit-transfer.yaml"
$seqDocCache = Join-Path $seqDocRoot ".seqdoc/credit-transfer/cache-v1.db"
$seqDocOutput = Join-Path $seqDocRoot ".seqdoc/credit-transfer/output"

dotnet run --project src/SeqDoc.Cli --configuration Release --no-build -- analyze `
  $creditTransferProject `
  --repository-root $creditTransferRoot `
  --config $seqDocConfig `
  --configuration Release `
  --framework net9.0 `
  --cache $seqDocCache `
  --output $seqDocOutput
```

For the accepted source revision this produces 18 files: an index plus Markdown and Mermaid for eight flows. The command
was verified against the standard sibling checkout and produced byte-identical files to the reviewed
`SeqDoc-CreditTransfer-Hardened` suite.

Open `.seqdoc/credit-transfer/output/index.md` and preview the Markdown in an editor with Mermaid support. The `.mmd`
files can also be rendered with Mermaid CLI.

## Configuration example

```yaml
schemaVersion: 1

documentation:
  excludeParticipants:
    - Exact.Namespace.CustomLogger
  excludeCalls:
    - Exact.Namespace.TelemetryClient.Track
    - Exact.Namespace.NoisyType.*

selection:
  roots:
    - method:v1:<complete-cataloged-id>
```

Exclusions affect presentation, not analysis facts. Structural root/client participants cannot be excluded.

## Troubleshooting

### The project cannot be found

Run from the SeqDoc root and verify the sibling folder name. Alternatively:

```powershell
$env:SEQDOC_TEST_PROJECTS_ROOT = "D:\path\to\SeqDoc-TestProjects"
```

### A configured root is unknown

The source revision, project, framework, or MethodId differs. Run `catalog` again for that exact profile and update the
configuration. Do not shorten the ID.

### MSBuild or package warnings appear

SeqDoc may continue with conservative diagnostics when a project loads partially. Restore/build the target directly,
select the intended SDK/framework, and review every `SD1101` warning before trusting coverage.

### Output appears stale

Use a new cache/output directory or remove the ignored `.seqdoc/` directory, then analyze again. A failed analysis does
not replace the previous valid active state.

### Diagrams are truncated or sparse

Read Technical fallback and diagnostic artifacts. SeqDoc withholds behavior when exact call, body, predicate, profile,
or topology evidence is unavailable; it does not invent missing behavior.
