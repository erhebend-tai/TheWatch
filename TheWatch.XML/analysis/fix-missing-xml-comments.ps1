<#
.SYNOPSIS
Automatically adds XML documentation stubs to public/internal C# members.

.DESCRIPTION
Reads the coverage report from analyze-xml-coverage.ps1 and adds XML comment
stubs to all identified missing documentation items. This is a convenience tool
to quickly scaffold documentation that developers can then fill in.

Generates stubs like:
    /// <summary>
    /// [INSERT DESCRIPTION HERE]
    /// </summary>
    public class MyClass

    /// <summary>
    /// [INSERT DESCRIPTION HERE]
    /// </summary>
    public void MyMethod()

.PARAMETER ReportPath
Path to the JSON coverage report from analyze-xml-coverage.ps1
Default: ./coverage-report.json

.PARAMETER Confirm
Requires confirmation before modifying files

.PARAMETER DryRun
Show changes without modifying files

.EXAMPLE
./fix-missing-xml-comments.ps1
./fix-missing-xml-comments.ps1 -ReportPath ./XML/analysis/coverage-report.json -DryRun
./fix-missing-xml-comments.ps1 -Confirm
#>

param(
    [string]$ReportPath = "./coverage-report.json",
    [switch]$Confirm,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# Ensure we're at repo root
$RepoRoot = git rev-parse --show-toplevel 2>$null
if (-not $RepoRoot) {
    $RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
}

$SrcPath = Join-Path $RepoRoot "src"

if (-not (Test-Path $ReportPath)) {
    Write-Error "Coverage report not found: $ReportPath"
    exit 1
}

Write-Host "Loading coverage report from: $ReportPath" -ForegroundColor Cyan

try {
    $report = Get-Content $ReportPath -Encoding UTF8 | ConvertFrom-Json
} catch {
    Write-Error "Failed to parse JSON report: $_"
    exit 1
}

$totalMissing = $report.totalMissing
$filesToFix = $report.missingDetails.Count

Write-Host "Found $filesToFix files with $totalMissing missing documentation items" -ForegroundColor Yellow

if ($Confirm) {
    $response = Read-Host "Proceed with adding XML stubs? (yes/no)"
    if ($response -ne "yes") {
        Write-Host "Cancelled." -ForegroundColor Gray
        exit 0
    }
}

$filesModified = 0
$itemsAdded = 0

foreach ($fileEntry in $report.missingDetails) {
    $filePath = Join-Path $SrcPath $fileEntry.path

    if (-not (Test-Path $filePath)) {
        Write-Warning "File not found: $filePath"
        continue
    }

    try {
        $lines = @(Get-Content $filePath -Encoding UTF8)
        $modified = $false
        $newLines = @()
        $i = 0

        while ($i -lt $lines.Count) {
            $line = $lines[$i]
            $currentLineNum = $i + 1

            # Check if any of this file's missing items match the current line number
            $missingItem = $fileEntry.items | Where-Object { $_.lineNumber -eq $currentLineNum }

            if ($missingItem) {
                # Check if previous line already has XML comment
                if ($i -gt 0) {
                    $prevLine = $lines[$i - 1]
                    if ($prevLine -match "^\s*///\s*<") {
                        $newLines += $line
                        $i++
                        continue
                    }
                }

                # Add XML stub before the declaration
                $indent = if ($line -match "^(\s*)") { $matches[1] } else { "    " }
                $stub = @(
                    "$indent/// <summary>"
                    "$indent/// [INSERT DESCRIPTION HERE]"
                    "$indent/// </summary>"
                )

                $newLines += $stub
                $newLines += $line
                $modified = $true
                $itemsAdded++
            } else {
                $newLines += $line
            }

            $i++
        }

        if ($modified) {
            if ($DryRun) {
                Write-Host "Would modify: $($fileEntry.path) (+$($fileEntry.items.Count) stubs)" -ForegroundColor Blue
            } else {
                Set-Content -Path $filePath -Value $newLines -Encoding UTF8
                Write-Host "Modified: $($fileEntry.path) (+$($fileEntry.items.Count) stubs)" -ForegroundColor Green
                $filesModified++
            }
        }
    } catch {
        Write-Warning "Error processing file $filePath : $_"
    }
}

Write-Host "`n=== Summary ===" -ForegroundColor Green
Write-Host "Files modified: $filesModified"
Write-Host "XML stubs added: $itemsAdded"

if ($DryRun) {
    Write-Host "(DRY RUN - no files were actually modified)" -ForegroundColor Yellow
}

Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "1. Review the added stubs and fill in [INSERT DESCRIPTION HERE]"
Write-Host "2. Run analyze-xml-coverage.ps1 again to verify coverage improved"
Write-Host "3. Commit the changes"
