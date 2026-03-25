<#
.SYNOPSIS
Generates a Structure manifest XML from the codebase: solutions, projects, dependencies, layer topology.

.DESCRIPTION
Scans the repository for all structural elements and produces TheWatch.Structure.xml:
- Solution files (.sln, .slnf, .slnx) and their project membership
- Project files (.csproj) with target frameworks, SDK, output type
- Project-to-project references (dependency graph)
- NuGet package references with version and cross-project usage
- Layer topology derived from directory structure
- File counts per project (.cs, .razor, .xaml)

.PARAMETER OutputPath
Path to write the structure manifest. Default: ./XML/TheWatch.Structure.xml

.PARAMETER XsdPath
Path to the XSD schema for validation. Default: ./XML/TheWatchArchitecture.xsd

.PARAMETER Validate
Validate output against XSD after generation.

.PARAMETER DryRun
Show what would be generated without writing files.

.EXAMPLE
./generate-structure.ps1
./generate-structure.ps1 -Validate
./generate-structure.ps1 -DryRun
#>

param(
    [string]$OutputPath,
    [string]$XsdPath,
    [switch]$Validate,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$RepoRoot = git rev-parse --show-toplevel 2>$null
if (-not $RepoRoot) { $RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName }

$SrcPath = Join-Path $RepoRoot "src"
if (-not $OutputPath) { $OutputPath = Join-Path $RepoRoot "XML/TheWatch.Structure.xml" }
if (-not $XsdPath)    { $XsdPath    = Join-Path $RepoRoot "XML/TheWatchArchitecture.xsd" }

Write-Host "=== TheWatch Structure Generator ===" -ForegroundColor Green
Write-Host "Repository: $RepoRoot" -ForegroundColor Gray

# ── Helpers ──────────────────────────────────────────────────────────────────

function Get-RelativePath {
    param([string]$Full, [string]$Base = $RepoRoot)
    $Full -replace [regex]::Escape($Base), "" -replace "^[\\/]", ""
}

function Get-LayerFromPath {
    param([string]$FilePath)
    $rel = Get-RelativePath $FilePath $SrcPath
    $parts = $rel -split "[\\/]"
    if ($parts.Count -ge 1) { return $parts[0] }
    return "Root"
}

# ── Scan Solutions ───────────────────────────────────────────────────────────

Write-Host "`nScanning solution files..." -ForegroundColor Cyan

$slnFiles = @(
    Get-ChildItem -Path $RepoRoot -Filter "*.sln"  -ErrorAction SilentlyContinue
    Get-ChildItem -Path $RepoRoot -Filter "*.slnf" -ErrorAction SilentlyContinue
    Get-ChildItem -Path $RepoRoot -Filter "*.slnx" -ErrorAction SilentlyContinue
)
# Also check tools/
$toolsPath = Join-Path $RepoRoot "tools"
if (Test-Path $toolsPath) {
    $slnFiles += @(
        Get-ChildItem -Path $toolsPath -Filter "*.sln"  -Recurse -ErrorAction SilentlyContinue
        Get-ChildItem -Path $toolsPath -Filter "*.slnf" -Recurse -ErrorAction SilentlyContinue
        Get-ChildItem -Path $toolsPath -Filter "*.slnx" -Recurse -ErrorAction SilentlyContinue
    )
}

$solutions = [System.Collections.ArrayList]::new()

foreach ($sln in $slnFiles) {
    $slnRel = Get-RelativePath $sln.FullName
    $ext = $sln.Extension.TrimStart('.')
    $slnName = [System.IO.Path]::GetFileNameWithoutExtension($sln.Name)

    $projectRefs = [System.Collections.ArrayList]::new()

    if ($ext -eq "sln") {
        # Parse .sln for Project lines
        try {
            $content = Get-Content $sln.FullName -Encoding UTF8 -ErrorAction SilentlyContinue
            foreach ($line in $content) {
                if ($line -match 'Project\("\{[^}]+\}"\)\s*=\s*"([^"]+)"\s*,\s*"([^"]+)"') {
                    [void]$projectRefs.Add(@{ name = $Matches[1]; path = $Matches[2] })
                }
            }
        } catch { }
    }
    elseif ($ext -eq "slnf") {
        # Parse .slnf (JSON solution filter)
        try {
            $json = Get-Content $sln.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($json.solution.projects) {
                foreach ($p in $json.solution.projects) {
                    $pName = [System.IO.Path]::GetFileNameWithoutExtension($p)
                    [void]$projectRefs.Add(@{ name = $pName; path = $p })
                }
            }
        } catch { }
    }

    [void]$solutions.Add(@{
        name = $slnName; path = $slnRel; type = $ext; projectCount = $projectRefs.Count
        projects = $projectRefs
    })
}

Write-Host "  Solutions: $($solutions.Count)" -ForegroundColor Cyan

# ── Scan Projects ────────────────────────────────────────────────────────────

Write-Host "`nScanning .csproj files..." -ForegroundColor Cyan

$csprojFiles = @(Get-ChildItem -Path $SrcPath -Filter "*.csproj" -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]' })

$projects = [System.Collections.ArrayList]::new()
$allPackages = @{}  # package name -> list of projects using it
$dependencyEdges = [System.Collections.ArrayList]::new()
$frameworkCounts = @{}

foreach ($csproj in $csprojFiles) {
    try {
        $content = Get-Content $csproj.FullName -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        if (-not $content) { continue }

        $projName = [System.IO.Path]::GetFileNameWithoutExtension($csproj.Name)
        $projRel = Get-RelativePath $csproj.FullName $SrcPath
        $projDir = $csproj.DirectoryName
        $layer = Get-LayerFromPath $csproj.FullName

        # Target framework
        $tfm = ""
        if ($content -match '<TargetFramework>\s*(\S+)\s*</TargetFramework>') { $tfm = $Matches[1] }
        elseif ($content -match '<TargetFrameworks>\s*(\S+)\s*</TargetFrameworks>') { $tfm = $Matches[1] }
        if ($tfm) {
            if (-not $frameworkCounts[$tfm]) { $frameworkCounts[$tfm] = 0 }
            $frameworkCounts[$tfm]++
        }

        # Output type
        $outputType = ""
        if ($content -match '<OutputType>\s*(\S+)\s*</OutputType>') { $outputType = $Matches[1] }

        # SDK
        $sdk = ""
        if ($content -match '<Project\s+Sdk="([^"]+)"') { $sdk = $Matches[1] }

        # File counts
        $csCount = @(Get-ChildItem -Path $projDir -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }).Count
        $razorCount = @(Get-ChildItem -Path $projDir -Filter "*.razor" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }).Count
        $xamlCount = @(Get-ChildItem -Path $projDir -Filter "*.xaml" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }).Count

        # Project references
        $projRefs = [System.Collections.ArrayList]::new()
        $refMatches = [regex]::Matches($content, '<ProjectReference\s+Include="([^"]+)"')
        foreach ($m in $refMatches) {
            $refPath = $m.Groups[1].Value
            $refName = [System.IO.Path]::GetFileNameWithoutExtension($refPath)
            [void]$projRefs.Add(@{ name = $refName; path = $refPath })
            [void]$dependencyEdges.Add(@{ from = $projName; to = $refName; type = "ProjectReference" })
        }

        # Package references
        $pkgRefs = [System.Collections.ArrayList]::new()
        $pkgMatches = [regex]::Matches($content, '<PackageReference\s+Include="([^"]+)"\s*(?:Version="([^"]*)")?')
        foreach ($m in $pkgMatches) {
            $pkgName = $m.Groups[1].Value
            $pkgVer = if ($m.Groups[2].Success) { $m.Groups[2].Value } else { "" }
            [void]$pkgRefs.Add(@{ name = $pkgName; version = $pkgVer })

            # Track cross-project package usage
            if (-not $allPackages[$pkgName]) {
                $allPackages[$pkgName] = @{ version = $pkgVer; users = [System.Collections.ArrayList]::new() }
            }
            [void]$allPackages[$pkgName].users.Add($projName)
        }

        [void]$projects.Add(@{
            name = $projName; path = $projRel; layer = $layer
            targetFramework = $tfm; outputType = $outputType; sdk = $sdk
            csFileCount = $csCount; razorFileCount = $razorCount; xamlFileCount = $xamlCount
            projectRefs = $projRefs; packageRefs = $pkgRefs
        })
    } catch {
        Write-Verbose "Error processing $($csproj.FullName): $_"
    }
}

Write-Host "  Projects: $($projects.Count)" -ForegroundColor Cyan
Write-Host "  Dependency Edges: $($dependencyEdges.Count)" -ForegroundColor Cyan
Write-Host "  Unique NuGet Packages: $($allPackages.Count)" -ForegroundColor Cyan

# ── Layer Grouping ───────────────────────────────────────────────────────────

$layers = @{}
foreach ($proj in $projects) {
    $l = $proj.layer
    if (-not $layers[$l]) { $layers[$l] = [System.Collections.ArrayList]::new() }
    [void]$layers[$l].Add($proj)
}

Write-Host "`n  Layers:" -ForegroundColor Green
foreach ($l in ($layers.Keys | Sort-Object)) {
    Write-Host "    $l`: $($layers[$l].Count) projects" -ForegroundColor Cyan
}

# ── Generate XML ─────────────────────────────────────────────────────────────

$xmlNs = "http://thewatch.io/architecture/2026-03"
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.IndentChars = "  "
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)

$stream = [System.IO.MemoryStream]::new()
$writer = [System.Xml.XmlWriter]::Create($stream, $settings)

$writer.WriteStartDocument()
$writer.WriteStartElement("StructureManifest", $xmlNs)
$writer.WriteAttributeString("name", "The Watch - Structure Manifest")
$writer.WriteAttributeString("totalProjects", $projects.Count.ToString())

# Metadata
$writer.WriteStartElement("Metadata", $xmlNs)
$writer.WriteAttributeString("generator", "generate-structure.ps1")
$writer.WriteAttributeString("generatedAt", (Get-Date -Format "o"))
$writer.WriteAttributeString("framework", "net10.0")
$writer.WriteAttributeString("sourceRoot", $SrcPath)
$writer.WriteEndElement()

# Solutions
$writer.WriteStartElement("Solutions", $xmlNs)
$writer.WriteAttributeString("count", $solutions.Count.ToString())
foreach ($sln in $solutions) {
    $writer.WriteStartElement("Solution", $xmlNs)
    $writer.WriteAttributeString("name", $sln.name)
    $writer.WriteAttributeString("path", $sln.path)
    $writer.WriteAttributeString("type", $sln.type)
    $writer.WriteAttributeString("projectCount", $sln.projectCount.ToString())
    foreach ($ref in $sln.projects) {
        $writer.WriteStartElement("ProjectRef", $xmlNs)
        $writer.WriteAttributeString("name", $ref.name)
        if ($ref.path) { $writer.WriteAttributeString("path", $ref.path) }
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Layers with nested projects
$writer.WriteStartElement("Layers", $xmlNs)
$writer.WriteAttributeString("count", $layers.Count.ToString())
foreach ($layerName in ($layers.Keys | Sort-Object)) {
    $layerProjects = $layers[$layerName]
    $writer.WriteStartElement("Layer", $xmlNs)
    $writer.WriteAttributeString("name", $layerName)
    $writer.WriteAttributeString("projectCount", $layerProjects.Count.ToString())

    foreach ($proj in ($layerProjects | Sort-Object -Property { $_.name })) {
        $writer.WriteStartElement("Project", $xmlNs)
        $writer.WriteAttributeString("name", $proj.name)
        $writer.WriteAttributeString("path", $proj.path)
        if ($proj.targetFramework) { $writer.WriteAttributeString("targetFramework", $proj.targetFramework) }
        if ($proj.outputType)      { $writer.WriteAttributeString("outputType", $proj.outputType) }
        if ($proj.sdk)             { $writer.WriteAttributeString("sdk", $proj.sdk) }
        if ($proj.csFileCount)     { $writer.WriteAttributeString("csFileCount", $proj.csFileCount.ToString()) }
        if ($proj.razorFileCount)  { $writer.WriteAttributeString("razorFileCount", $proj.razorFileCount.ToString()) }
        if ($proj.xamlFileCount)   { $writer.WriteAttributeString("xamlFileCount", $proj.xamlFileCount.ToString()) }

        foreach ($ref in $proj.projectRefs) {
            $writer.WriteStartElement("ProjectReference", $xmlNs)
            $writer.WriteAttributeString("name", $ref.name)
            if ($ref.path) { $writer.WriteAttributeString("path", $ref.path) }
            $writer.WriteEndElement()
        }
        foreach ($pkg in $proj.packageRefs) {
            $writer.WriteStartElement("PackageReference", $xmlNs)
            $writer.WriteAttributeString("name", $pkg.name)
            if ($pkg.version) { $writer.WriteAttributeString("version", $pkg.version) }
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Dependency Graph (flat edge list for visualization tools)
$writer.WriteStartElement("DependencyGraph", $xmlNs)
$uniqueNodes = ($dependencyEdges | ForEach-Object { $_.from; $_.to } | Select-Object -Unique)
$writer.WriteAttributeString("nodeCount", $uniqueNodes.Count.ToString())
$writer.WriteAttributeString("edgeCount", $dependencyEdges.Count.ToString())
foreach ($edge in $dependencyEdges) {
    $writer.WriteStartElement("Edge", $xmlNs)
    $writer.WriteAttributeString("from", $edge.from)
    $writer.WriteAttributeString("to", $edge.to)
    $writer.WriteAttributeString("type", $edge.type)
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# NuGet Packages (sorted by usage count descending)
$sortedPackages = $allPackages.GetEnumerator() | Sort-Object { $_.Value.users.Count } -Descending
$writer.WriteStartElement("NuGetPackages", $xmlNs)
$writer.WriteAttributeString("count", $allPackages.Count.ToString())
foreach ($pkg in $sortedPackages) {
    $writer.WriteStartElement("Package", $xmlNs)
    $writer.WriteAttributeString("name", $pkg.Key)
    if ($pkg.Value.version) { $writer.WriteAttributeString("version", $pkg.Value.version) }
    $writer.WriteAttributeString("usageCount", $pkg.Value.users.Count.ToString())
    foreach ($user in $pkg.Value.users) {
        $writer.WriteStartElement("UsedBy", $xmlNs)
        $writer.WriteAttributeString("project", $user)
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Target Frameworks
$writer.WriteStartElement("Frameworks", $xmlNs)
foreach ($fw in ($frameworkCounts.GetEnumerator() | Sort-Object { $_.Value } -Descending)) {
    $writer.WriteStartElement("Framework", $xmlNs)
    $writer.WriteAttributeString("name", $fw.Key)
    $writer.WriteAttributeString("projectCount", $fw.Value.ToString())
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

$writer.WriteEndElement() # StructureManifest
$writer.WriteEndDocument()
$writer.Flush()
$writer.Close()

$xmlContent = [System.Text.Encoding]::UTF8.GetString($stream.ToArray())
$stream.Close()

if ($DryRun) {
    Write-Host "`n(DRY RUN) Would write to: $OutputPath" -ForegroundColor Yellow
} else {
    [System.IO.File]::WriteAllText($OutputPath, $xmlContent, [System.Text.UTF8Encoding]::new($false))
    Write-Host "`nGenerated: $OutputPath" -ForegroundColor Green
}

# ── Validate ─────────────────────────────────────────────────────────────────

if ($Validate -and (Test-Path $XsdPath) -and -not $DryRun) {
    Write-Host "`nValidating against XSD..." -ForegroundColor Cyan
    try {
        $schema = [System.Xml.Schema.XmlSchemaSet]::new()
        $schema.Add("http://thewatch.io/architecture/2026-03", $XsdPath) | Out-Null
        [xml]$doc = Get-Content $OutputPath -Encoding UTF8
        $doc.Schemas = $schema
        $validationErrors = @()
        $doc.Validate({ param($sender, $e) $validationErrors += $e.Message })
        if ($validationErrors.Count -eq 0) {
            Write-Host "  XSD validation passed" -ForegroundColor Green
        } else {
            Write-Host "  XSD validation warnings: $($validationErrors.Count)" -ForegroundColor Yellow
            $validationErrors | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
        }
    } catch {
        Write-Warning "Validation error: $_"
    }
}

Write-Host "`n=== Structure Generation Complete ===" -ForegroundColor Green
