<#
.SYNOPSIS
Orchestrates the XML architecture generation pipeline with support for targeted manifests.

.DESCRIPTION
Master orchestrator that can run the complete pipeline or individual generators:

Modes:
  all        - Run full pipeline: analyze -> fix -> generate all manifests -> validate
  full       - Generate only TheWatchArchitecture.xml (full architecture doc)
  functions  - Generate only TheWatch.Functions.xml (handlers, endpoints, jobs, services)
  ui         - Generate only TheWatch.UI.xml (XAML pages, Blazor components, styles)
  structure  - Generate only TheWatch.Structure.xml (projects, dependencies, layers)
  analyze    - Run XML documentation coverage analysis only
  fix        - Add missing XML documentation stubs only

.PARAMETER Mode
What to generate. Default: all

.PARAMETER SkipBuild
Skip solution compilation during the full architecture generation.

.PARAMETER AutoFix
Automatically add XML stubs without prompting.

.PARAMETER Validate
Validate generated output against XSD. Default when Mode=all.

.PARAMETER Commit
Commit changes to git after successful generation.

.PARAMETER CommitMessage
Custom commit message (implies -Commit).

.PARAMETER Parallel
Run independent generators concurrently (functions, ui, structure).

.PARAMETER DryRun
Show changes without modifying files.

.EXAMPLE
./orchestrate-generation.ps1
./orchestrate-generation.ps1 -Mode functions
./orchestrate-generation.ps1 -Mode ui -Validate
./orchestrate-generation.ps1 -Mode structure -DryRun
./orchestrate-generation.ps1 -Mode all -Parallel -Validate
./orchestrate-generation.ps1 -Mode all -AutoFix -Commit
#>

param(
    [ValidateSet("all", "full", "functions", "ui", "structure", "analyze", "fix")]
    [string]$Mode = "all",
    [switch]$SkipBuild,
    [switch]$AutoFix,
    [switch]$Validate,
    [switch]$Commit,
    [string]$CommitMessage,
    [switch]$Parallel,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$RepoRoot = git rev-parse --show-toplevel 2>$null
if (-not $RepoRoot) { $RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName }

$XmlPath = Join-Path $RepoRoot "XML"
$AnalysisScripts  = Join-Path $XmlPath "analysis"
$GeneratorScripts = Join-Path $XmlPath "generators"

# Track timing
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# ── Banner ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  TheWatch Architecture Generation Pipeline" -ForegroundColor Cyan
Write-Host "  ─────────────────────────────────────────" -ForegroundColor DarkCyan
Write-Host "  Mode:     $Mode" -ForegroundColor Gray
Write-Host "  Parallel: $Parallel" -ForegroundColor Gray
Write-Host "  Validate: $Validate" -ForegroundColor Gray
Write-Host "  DryRun:   $DryRun" -ForegroundColor Gray
Write-Host ""

$generatedFiles = [System.Collections.ArrayList]::new()
$errors = [System.Collections.ArrayList]::new()

# ── Helper: Run a generator ──────────────────────────────────────────────────

function Invoke-Generator {
    param(
        [string]$Name,
        [string]$Script,
        [hashtable]$Params = @{}
    )

    $scriptPath = Join-Path $GeneratorScripts $Script
    if (-not (Test-Path $scriptPath)) {
        Write-Warning "Generator not found: $scriptPath"
        return $false
    }

    Write-Host "  [$Name] Starting..." -ForegroundColor Cyan
    $genSw = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        & $scriptPath @Params
        $genSw.Stop()
        Write-Host "  [$Name] Completed in $($genSw.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
        return $true
    } catch {
        $genSw.Stop()
        Write-Host "  [$Name] FAILED: $_" -ForegroundColor Red
        [void]$errors.Add("$Name`: $_")
        return $false
    }
}

# ── PHASE 1: Analysis (if all or analyze) ───────────────────────────────────

if ($Mode -in @("all", "analyze", "fix")) {
    Write-Host "Phase 1: XML Documentation Analysis" -ForegroundColor Cyan
    Write-Host ("─" * 50) -ForegroundColor DarkCyan

    $coverageScript = Join-Path $AnalysisScripts "analyze-xml-coverage.ps1"
    $coverageReport = Join-Path $AnalysisScripts "coverage-report.json"

    if (Test-Path $coverageScript) {
        Push-Location $RepoRoot
        try {
            & $coverageScript -ReportPath $coverageReport
            $analysisExitCode = $LASTEXITCODE

            if ($analysisExitCode -eq 0) {
                Write-Host "  Analysis: Full coverage!" -ForegroundColor Green
            } else {
                Write-Host "  Analysis: Missing documentation found" -ForegroundColor Yellow
                if ($Mode -eq "all" -and $AutoFix) {
                    $fixScript = Join-Path $AnalysisScripts "fix-missing-xml-comments.ps1"
                    if (Test-Path $fixScript) {
                        & $fixScript -ReportPath $coverageReport -DryRun:$DryRun
                    }
                }
            }
        } finally { Pop-Location }
    } else {
        Write-Warning "Coverage analysis script not found: $coverageScript"
    }

    if ($Mode -in @("analyze", "fix")) {
        $sw.Stop()
        Write-Host "`nCompleted in $($sw.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
        exit 0
    }

    Write-Host ""
}

# ── PHASE 2: Fix Missing XML (if requested) ─────────────────────────────────

if ($Mode -eq "fix" -or ($Mode -eq "all" -and $AutoFix)) {
    Write-Host "Phase 2: Adding XML Documentation Stubs" -ForegroundColor Cyan
    Write-Host ("─" * 50) -ForegroundColor DarkCyan

    $fixScript = Join-Path $AnalysisScripts "fix-missing-xml-comments.ps1"
    $coverageReport = Join-Path $AnalysisScripts "coverage-report.json"

    if ((Test-Path $fixScript) -and (Test-Path $coverageReport)) {
        Push-Location $RepoRoot
        try { & $fixScript -ReportPath $coverageReport -DryRun:$DryRun }
        finally { Pop-Location }
    }

    if ($Mode -eq "fix") {
        $sw.Stop()
        Write-Host "`nCompleted in $($sw.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Green
        exit 0
    }

    Write-Host ""
}

# ── PHASE 3: Generation ─────────────────────────────────────────────────────

Write-Host "Phase 3: Generating Manifests" -ForegroundColor Cyan
Write-Host ("─" * 50) -ForegroundColor DarkCyan

# Determine which generators to run
$generators = @()

switch ($Mode) {
    "full" {
        $generators = @(@{
            Name = "Full Architecture"
            Script = "generate-architecture-from-xml.ps1"
            Params = @{
                Configuration = "Release"
                SkipBuild = $SkipBuild
                OutputPath = (Join-Path $XmlPath "TheWatchArchitecture.xml")
                Validate = $Validate
                DryRun = $DryRun
            }
            Output = "TheWatchArchitecture.xml"
        })
    }
    "functions" {
        $generators = @(@{
            Name = "Functions"
            Script = "generate-functions.ps1"
            Params = @{ Validate = $Validate; DryRun = $DryRun }
            Output = "TheWatch.Functions.xml"
        })
    }
    "ui" {
        $generators = @(@{
            Name = "UI"
            Script = "generate-ui.ps1"
            Params = @{ Validate = $Validate; DryRun = $DryRun }
            Output = "TheWatch.UI.xml"
        })
    }
    "structure" {
        $generators = @(@{
            Name = "Structure"
            Script = "generate-structure.ps1"
            Params = @{ Validate = $Validate; DryRun = $DryRun }
            Output = "TheWatch.Structure.xml"
        })
    }
    "all" {
        $generators = @(
            @{
                Name = "Full Architecture"
                Script = "generate-architecture-from-xml.ps1"
                Params = @{
                    Configuration = "Release"
                    SkipBuild = $SkipBuild
                    OutputPath = (Join-Path $XmlPath "TheWatchArchitecture.xml")
                    Validate = $Validate
                    DryRun = $DryRun
                }
                Output = "TheWatchArchitecture.xml"
            }
            @{
                Name = "Functions"
                Script = "generate-functions.ps1"
                Params = @{ Validate = $Validate; DryRun = $DryRun }
                Output = "TheWatch.Functions.xml"
            }
            @{
                Name = "UI"
                Script = "generate-ui.ps1"
                Params = @{ Validate = $Validate; DryRun = $DryRun }
                Output = "TheWatch.UI.xml"
            }
            @{
                Name = "Structure"
                Script = "generate-structure.ps1"
                Params = @{ Validate = $Validate; DryRun = $DryRun }
                Output = "TheWatch.Structure.xml"
            }
        )
    }
}

if ($Parallel -and $generators.Count -gt 1) {
    # Run generators concurrently using PowerShell jobs
    Write-Host "  Running $($generators.Count) generators in parallel..." -ForegroundColor Cyan

    $jobs = @()
    foreach ($gen in $generators) {
        $scriptPath = Join-Path $GeneratorScripts $gen.Script
        $params = $gen.Params
        $jobs += Start-Job -ScriptBlock {
            param($ScriptPath, $Params)
            & $ScriptPath @Params
        } -ArgumentList $scriptPath, $params
    }

    # Wait for all jobs
    $jobs | Wait-Job | ForEach-Object {
        $output = Receive-Job $_
        if ($output) { Write-Host $output }
        Remove-Job $_
    }

    foreach ($gen in $generators) {
        $outFile = Join-Path $XmlPath $gen.Output
        if (Test-Path $outFile) {
            [void]$generatedFiles.Add($gen.Output)
            Write-Host "  [$($gen.Name)] Generated" -ForegroundColor Green
        }
    }
} else {
    # Run sequentially
    foreach ($gen in $generators) {
        $success = Invoke-Generator -Name $gen.Name -Script $gen.Script -Params $gen.Params
        if ($success) {
            $outFile = Join-Path $XmlPath $gen.Output
            if (Test-Path $outFile) {
                [void]$generatedFiles.Add($gen.Output)
            }
        }
    }
}

Write-Host ""

# ── PHASE 4: Commit (if requested) ──────────────────────────────────────────

if ($Commit -or $CommitMessage) {
    Write-Host "Phase 4: Committing Changes" -ForegroundColor Cyan
    Write-Host ("─" * 50) -ForegroundColor DarkCyan

    if (-not $DryRun) {
        Push-Location $RepoRoot
        try {
            $xmlFiles = @(
                "XML/TheWatchArchitecture.xml"
                "XML/TheWatch.Functions.xml"
                "XML/TheWatch.UI.xml"
                "XML/TheWatch.Structure.xml"
                "XML/analysis/coverage-report.json"
            )

            $changedFiles = @()
            foreach ($f in $xmlFiles) {
                $status = & git status --porcelain -- $f 2>$null
                if ($status) { $changedFiles += $f }
            }

            if ($changedFiles.Count -gt 0) {
                Write-Host "  Changed files:" -ForegroundColor Gray
                $changedFiles | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }

                $msg = if ($CommitMessage) { $CommitMessage }
                       else { "docs: regenerate architecture manifests ($($generatedFiles -join ', '))" }

                & git add @changedFiles
                & git commit -m $msg

                if ($LASTEXITCODE -eq 0) {
                    Write-Host "  Committed successfully" -ForegroundColor Green
                } else {
                    Write-Warning "  Git commit failed"
                }
            } else {
                Write-Host "  No changes to commit" -ForegroundColor Yellow
            }
        } finally { Pop-Location }
    } else {
        Write-Host "  (Dry run - no commits)" -ForegroundColor Yellow
    }

    Write-Host ""
}

# ── Summary ──────────────────────────────────────────────────────────────────

$sw.Stop()

Write-Host ("=" * 50) -ForegroundColor Green
Write-Host "  Generation Pipeline Complete" -ForegroundColor Green
Write-Host ("=" * 50) -ForegroundColor Green

Write-Host "`n  Generated manifests:" -ForegroundColor Cyan
foreach ($f in $generatedFiles) {
    Write-Host "    XML/$f" -ForegroundColor Green
}

if ($errors.Count -gt 0) {
    Write-Host "`n  Errors:" -ForegroundColor Red
    foreach ($e in $errors) {
        Write-Host "    $e" -ForegroundColor Red
    }
}

Write-Host "`n  Elapsed: $($sw.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Gray

Write-Host "`n  Usage examples:" -ForegroundColor Gray
Write-Host "    ./orchestrate-generation.ps1 -Mode functions    # Just behavioral elements"
Write-Host "    ./orchestrate-generation.ps1 -Mode ui           # Just UI components"
Write-Host "    ./orchestrate-generation.ps1 -Mode structure    # Just project topology"
Write-Host "    ./orchestrate-generation.ps1 -Mode full         # Full architecture doc"
Write-Host "    ./orchestrate-generation.ps1 -Mode all -Parallel # Everything, concurrently"
Write-Host ""
