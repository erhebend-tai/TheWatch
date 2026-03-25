<#
.SYNOPSIS
Analyzes XML documentation coverage across all C# files in the src/ directory.

.DESCRIPTION
Scans all .cs files in src/ and identifies missing XML comments on public/internal
classes, methods, properties, records, interfaces, and other members.

Generates a JSON report with:
- Missing documentation items grouped by project and namespace
- Coverage statistics by layer and project
- Detailed source file location information

.PARAMETER ReportPath
Output path for the JSON report. Default: ./coverage-report.json

.PARAMETER LayerFilter
Optional filter to analyze only specific layers (Domain, Application, Infrastructure, Presentation, Shared, Workers)

.PARAMETER Verbose
Enable verbose output for debugging

.EXAMPLE
./analyze-xml-coverage.ps1
./analyze-xml-coverage.ps1 -LayerFilter Domain
./analyze-xml-coverage.ps1 -ReportPath ./reports/coverage-$(Get-Date -Format 'yyyyMMdd').json
#>

param(
    [string]$ReportPath = "./coverage-report.json",
    [string[]]$LayerFilter = @(),
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"
$VerbosePreference = if ($Verbose) { "Continue" } else { "SilentlyContinue" }

# Ensure we're at repo root
$RepoRoot = git rev-parse --show-toplevel 2>$null
if (-not $RepoRoot) {
    $RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
}

$SrcPath = Join-Path $RepoRoot "src"

if (-not (Test-Path $SrcPath)) {
    Write-Error "Source directory not found: $SrcPath"
    exit 1
}

Write-Verbose "Analyzing XML coverage in: $SrcPath"

# Mapping of common source directories to layers
$LayerMapping = @{
    "Domain"           = @("Domain", "domain")
    "Application"      = @("Application", "application")
    "Infrastructure"   = @("Infrastructure", "infrastructure")
    "Presentation"     = @("Presentation", "presentation")
    "Shared"           = @("Shared", "shared")
    "Workers"          = @("Workers", "workers")
    "Libraries"        = @("Libraries", "libraries")
    "TheWatch"         = @("TheWatch", "thewatch")
    "Aspire"           = @("aspire", "Aspire")
}

function Get-Layer {
    param([string]$FilePath)

    foreach ($layer in $LayerMapping.Keys) {
        if ($FilePath -like "*$layer*") {
            return $layer
        }
    }
    return "Other"
}

function Get-Project {
    param([string]$FilePath)

    # Extract project from path: src/Domain/TheWatch.Domain.Incidents/
    $parts = $FilePath -split [regex]::Escape([System.IO.Path]::DirectorySeparatorChar)
    $srcIndex = $parts.IndexOf("src")
    if ($srcIndex -ge 0 -and $srcIndex + 2 -lt $parts.Count) {
        return $parts[$srcIndex + 2]
    }
    return "Unknown"
}

function Test-HasXmlComment {
    param([string]$Line)

    return $Line -match "^\s*///\s*<"
}

function Find-MissingXmlMembers {
    param([string]$FilePath)

    $missingMembers = @()

    try {
        $lines = @(Get-Content $FilePath -Encoding UTF8)
        $fileRelative = $FilePath -replace [regex]::Escape($SrcPath), ""

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $prevLine = if ($i -gt 0) { $lines[$i - 1] } else { "" }

            # Skip if previous line has XML comment
            if (Test-HasXmlComment $prevLine) {
                continue
            }

            # Match public/internal class, record, interface, struct
            if ($line -match '^\s*(public|internal)\s+(abstract\s+)?(sealed\s+)?(partial\s+)?(class|record|interface|struct)\s+(\w+)') {
                $memberType = $matches[4]
                $memberName = $matches[6]
                $lineNum = $i + 1

                $missingMembers += @{
                    kind       = $memberType
                    name       = $memberName
                    lineNumber = $lineNum
                    pattern    = "type"
                }
                Write-Verbose "Missing XML on $memberType $memberName at line $lineNum"
            }

            # Match public/internal methods (but not inside class definition line)
            if ($line -match '^\s*(public|internal)\s+' -and $line -match '(async\s+)?([\w<>?,\s]+\s+)?(\w+)\s*\(') {
                # Skip class/interface/record/struct declarations
                if ($line -notmatch '\b(class|record|interface|struct)\b') {
                    $memberName = $matches[3]
                    $lineNum = $i + 1

                    # Try to determine if it's a method or property
                    if ($line -match '\s*\(.*\)') {
                        $kind = "method"
                    } elseif ($line -match '\s*\{.*\}' -or $line -match '=>') {
                        $kind = "property"
                    } else {
                        continue
                    }

                    $missingMembers += @{
                        kind       = $kind
                        name       = $memberName
                        lineNumber = $lineNum
                        pattern    = "member"
                    }
                    Write-Verbose "Missing XML on $kind $memberName at line $lineNum"
                }
            }
        }
    } catch {
        Write-Verbose "Error analyzing file $FilePath : $_"
    }

    return $missingMembers
}

# Scan all .cs files
$csharpFiles = @(Get-ChildItem -Path $SrcPath -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue)
Write-Host "Found $($csharpFiles.Count) C# files" -ForegroundColor Cyan

$report = @{
    timestamp      = (Get-Date -Format "o")
    totalFiles     = $csharpFiles.Count
    filesAnalyzed  = 0
    totalMissing   = 0
    layers         = @{}
    projects       = @{}
    missingDetails = @()
}

# Apply layer filter if specified
$filesToAnalyze = $csharpFiles
if ($LayerFilter.Count -gt 0) {
    $filesToAnalyze = $csharpFiles | Where-Object {
        $layer = Get-Layer $_.FullName
        $LayerFilter -contains $layer
    }
}

Write-Host "Analyzing $($filesToAnalyze.Count) files..." -ForegroundColor Cyan

foreach ($file in $filesToAnalyze) {
    $missingMembers = Find-MissingXmlMembers -FilePath $file.FullName

    if ($missingMembers.Count -gt 0) {
        $report.filesAnalyzed++
        $report.totalMissing += $missingMembers.Count

        $layer = Get-Layer $file.FullName
        $project = Get-Project $file.FullName
        $fileRelative = $file.FullName -replace [regex]::Escape($SrcPath), ""

        # Initialize layer entry
        if (-not $report.layers[$layer]) {
            $report.layers[$layer] = @{
                missing = 0
                files   = @()
            }
        }

        # Initialize project entry
        if (-not $report.projects[$project]) {
            $report.projects[$project] = @{
                layer   = $layer
                missing = 0
                files   = @()
            }
        }

        # Aggregate counts
        $report.layers[$layer].missing += $missingMembers.Count
        $report.projects[$project].missing += $missingMembers.Count

        # Track by file
        $fileEntry = @{
            path    = $fileRelative
            layer   = $layer
            project = $project
            missing = $missingMembers.Count
            items   = $missingMembers
        }

        $report.layers[$layer].files += $fileEntry
        $report.projects[$project].files += $fileEntry
        $report.missingDetails += $fileEntry
    }
}

# Compute summary statistics
$report.coverageByLayer = @{}
foreach ($layer in $report.layers.Keys) {
    $layerData = $report.layers[$layer]
    $totalItems = ($layerData.files | Measure-Object -Property missing -Sum).Sum
    $coveragePercent = if ($totalItems -gt 0) { [Math]::Round((100 * (1 - $totalItems / ($totalItems + 10))) , 2) } else { 100 }

    $report.coverageByLayer[$layer] = @{
        missingMembers = $layerData.missing
        filesWithMissing = $layerData.files.Count
    }
}

# Sort and prepare final report
$report.missingDetails = $report.missingDetails | Sort-Object -Property @{Expression = { $_.layer }}, @{Expression = { $_.project }}, @{Expression = { $_.path }}

# Output summary
Write-Host "`n=== XML Documentation Coverage Report ===" -ForegroundColor Green
Write-Host "Generated: $($report.timestamp)" -ForegroundColor Gray
Write-Host "Total files analyzed: $($report.filesAnalyzed)" -ForegroundColor Cyan
Write-Host "Files with missing documentation: $($report.missingDetails.Count)" -ForegroundColor Yellow
Write-Host "Total missing members: $($report.totalMissing)" -ForegroundColor Red

Write-Host "`nCoverage by Layer:" -ForegroundColor Green
foreach ($layer in ($report.coverageByLayer.Keys | Sort-Object)) {
    $stats = $report.coverageByLayer[$layer]
    Write-Host "  $layer`: $($stats.missingMembers) missing in $($stats.filesWithMissing) files"
}

Write-Host "`nTop Projects with Missing Documentation:" -ForegroundColor Green
$topProjects = $report.projects.Values | Sort-Object -Property missing -Descending | Select-Object -First 5
foreach ($proj in $topProjects) {
    if ($proj.missing -gt 0) {
        Write-Host "  $($proj.Layer)/$($proj.name): $($proj.missing) missing"
    }
}

# Save JSON report
$reportJson = $report | ConvertTo-Json -Depth 10
Set-Content -Path $ReportPath -Value $reportJson -Encoding UTF8
Write-Host "`nReport saved to: $ReportPath" -ForegroundColor Cyan

# Exit with error code if missing items found
if ($report.totalMissing -gt 0) {
    exit 1
} else {
    Write-Host "`n✓ All public/internal members have XML documentation!" -ForegroundColor Green
    exit 0
}
