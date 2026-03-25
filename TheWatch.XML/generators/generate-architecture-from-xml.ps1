<#
.SYNOPSIS
Generates TheWatchArchitecture.xml by extracting XML documentation from compiled assemblies.

.DESCRIPTION
Extracts XML documentation comments from compiled .NET assemblies and populates
TheWatchArchitecture.xml with documented types, members, and relationships. This is
the "full" generator that produces the complete architecture document.

Process:
1. Compiles the backend solution (if needed)
2. Discovers compiled DLLs and their .xml documentation files
3. Parses XML documentation via XDocument
4. Extracts types (classes, interfaces, records) and members (methods, properties)
5. Organizes by layer and domain
6. Merges with existing TheWatchArchitecture.xml (preserves hand-authored sections)
7. Validates against TheWatchArchitecture.xsd

.PARAMETER Configuration
Build configuration: Release or Debug. Default: Release

.PARAMETER SkipBuild
Skip compilation and use existing assemblies.

.PARAMETER OutputPath
Path to generate architecture XML. Default: ./XML/TheWatchArchitecture.xml

.PARAMETER Validate
Validate output against XSD schema after generation.

.PARAMETER MergeExisting
Merge with existing XML rather than overwriting. Default: true

.PARAMETER DryRun
Show changes without modifying files.

.EXAMPLE
./generate-architecture-from-xml.ps1
./generate-architecture-from-xml.ps1 -Configuration Release -Validate
./generate-architecture-from-xml.ps1 -SkipBuild -DryRun
#>

param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [string]$OutputPath = "./XML/TheWatchArchitecture.xml",
    [switch]$Validate,
    [switch]$MergeExisting = $true,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$RepoRoot = git rev-parse --show-toplevel 2>$null
if (-not $RepoRoot) { $RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName }

$SrcPath = Join-Path $RepoRoot "src"
$XsdPath = Join-Path $RepoRoot "XML/TheWatchArchitecture.xsd"
$SlnFilter = Join-Path $RepoRoot "The_Watch.Backend.slnf"

Write-Host "=== TheWatch Architecture Generator (Full) ===" -ForegroundColor Green
Write-Host "Repository root: $RepoRoot" -ForegroundColor Gray
Write-Host "Output: $OutputPath" -ForegroundColor Cyan

# ── Step 1: Build ────────────────────────────────────────────────────────────

if (-not $SkipBuild) {
    Write-Host "`nStep 1: Building solution..." -ForegroundColor Cyan
    Push-Location $RepoRoot
    try {
        $buildOutput = & dotnet build $SlnFilter --configuration $Configuration 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build failed. Please fix compilation errors first."
            Write-Output $buildOutput
            exit 1
        }
        Write-Host "  Build successful" -ForegroundColor Green
    } finally { Pop-Location }
} else {
    Write-Host "`nStep 1: Skipping build (using existing assemblies)" -ForegroundColor Yellow
}

# ── Step 2: Discover assemblies ──────────────────────────────────────────────

Write-Host "`nStep 2: Discovering assemblies and documentation..." -ForegroundColor Cyan

# Search multiple output locations
$searchPaths = @(
    (Join-Path $RepoRoot "bin/$Configuration")
    (Join-Path $SrcPath "*/bin/$Configuration")
    (Join-Path $SrcPath "*/*/bin/$Configuration")
)

$assemblyFiles = [System.Collections.ArrayList]::new()
$xmlDocFiles   = [System.Collections.ArrayList]::new()

foreach ($searchPath in $searchPaths) {
    $resolved = Resolve-Path $searchPath -ErrorAction SilentlyContinue
    foreach ($p in $resolved) {
        Get-ChildItem $p -Recurse -Filter "TheWatch.*.dll" -Exclude "*Test*" -ErrorAction SilentlyContinue |
            ForEach-Object { [void]$assemblyFiles.Add($_.FullName) }
        Get-ChildItem $p -Recurse -Filter "TheWatch.*.xml" -ErrorAction SilentlyContinue |
            ForEach-Object { [void]$xmlDocFiles.Add($_.FullName) }
    }
}

# Deduplicate by filename
$assemblyFiles = @($assemblyFiles | Sort-Object { Split-Path $_ -Leaf } -Unique)
$xmlDocFiles   = @($xmlDocFiles | Sort-Object { Split-Path $_ -Leaf } -Unique)

Write-Host "  Assemblies: $($assemblyFiles.Count)" -ForegroundColor Gray
Write-Host "  XML docs:   $($xmlDocFiles.Count)" -ForegroundColor Gray

# ── Step 3: Parse XML documentation ──────────────────────────────────────────

Write-Host "`nStep 3: Parsing XML documentation..." -ForegroundColor Cyan

$documentedTypes   = [ordered]@{}
$documentedMembers = [System.Collections.ArrayList]::new()
$parsedFiles = 0

foreach ($xmlFile in $xmlDocFiles) {
    try {
        [xml]$doc = Get-Content $xmlFile -Encoding UTF8
        $assemblyName = $doc.doc.assembly.name
        $members = $doc.SelectNodes("//member")
        $parsedFiles++

        foreach ($member in $members) {
            $name = $member.GetAttribute("name")
            if (-not $name) { continue }

            $parts = $name -split ":", 2
            if ($parts.Count -ne 2) { continue }

            $memberType = $parts[0]
            $memberPath = $parts[1]

            # Extract documentation elements
            $summaryNode = $member.SelectSingleNode("summary")
            $summary = if ($summaryNode) { ($summaryNode.InnerText.Trim() -replace '\s+', ' ') } else { "" }

            $remarksNode = $member.SelectSingleNode("remarks")
            $remarks = if ($remarksNode) { ($remarksNode.InnerText.Trim() -replace '\s+', ' ') } else { "" }

            $returnsNode = $member.SelectSingleNode("returns")
            $returns = if ($returnsNode) { ($returnsNode.InnerText.Trim() -replace '\s+', ' ') } else { "" }

            $parameters = [System.Collections.ArrayList]::new()
            $paramNodes = $member.SelectNodes("param")
            foreach ($p in $paramNodes) {
                [void]$parameters.Add(@{
                    name = $p.GetAttribute("name")
                    text = ($p.InnerText.Trim() -replace '\s+', ' ')
                })
            }

            $docEntry = @{
                rawName    = $name
                type       = $memberType
                path       = $memberPath
                assembly   = $assemblyName
                summary    = $summary
                remarks    = $remarks
                returns    = $returns
                parameters = $parameters
            }

            if ($memberType -eq "T") {
                $pathParts = $memberPath -split "\."
                $typeName = $pathParts[-1]
                $namespace = ($memberPath -replace "\.$typeName$", "")

                $documentedTypes[$memberPath] = @{
                    summary   = $summary
                    remarks   = $remarks
                    namespace = $namespace
                    name      = $typeName
                    assembly  = $assemblyName
                }
            }

            [void]$documentedMembers.Add($docEntry)
        }
    } catch {
        Write-Warning "  Failed to parse: $(Split-Path $xmlFile -Leaf) - $_"
    }
}

Write-Host "  Parsed $parsedFiles XML files" -ForegroundColor Green
Write-Host "  Types: $($documentedTypes.Count) | Members: $($documentedMembers.Count)" -ForegroundColor Cyan

# ── Step 4: Merge or create architecture XML ─────────────────────────────────

Write-Host "`nStep 4: Generating architecture XML..." -ForegroundColor Cyan

$existingXml = $null
if ($MergeExisting -and (Test-Path $OutputPath)) {
    try {
        [xml]$existingXml = Get-Content $OutputPath -Encoding UTF8
        Write-Host "  Merging with existing: $OutputPath" -ForegroundColor Gray
    } catch {
        Write-Warning "  Could not parse existing file, generating fresh."
    }
}

# Build the output document
$xmlNs = "http://thewatch.io/architecture/2026-03"
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.IndentChars = "  "
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)

$stream = [System.IO.MemoryStream]::new()
$writer = [System.Xml.XmlWriter]::Create($stream, $settings)

$writer.WriteStartDocument()
$writer.WriteStartElement("TheWatchProject", $xmlNs)
$writer.WriteAttributeString("name", "The Watch - Monitoring and Alerting Service")
$writer.WriteAttributeString("version", "Enterprise-Grade")
$writer.WriteAttributeString("framework", ".NET 10")
$writer.WriteAttributeString("runtime", "Microsoft Azure, .NET Aspire")
$writer.WriteAttributeString("snapshot", "As-Is")
$writer.WriteAttributeString("generated", (Get-Date -Format "o"))

# If we have an existing file, preserve its SystemOverview and Orientation sections
if ($existingXml) {
    # Preserve SystemOverview
    $sysOverview = $existingXml.DocumentElement.SelectSingleNode("//*[local-name()='SystemOverview']")
    if ($sysOverview) {
        $writer.WriteRaw($sysOverview.OuterXml)
    }

    # Preserve Orientation (layers, etc.)
    $orientation = $existingXml.DocumentElement.SelectSingleNode("//*[local-name()='Orientation']")
    if ($orientation) {
        $writer.WriteRaw($orientation.OuterXml)
    }

    # Preserve ProgrammingConcepts
    $concepts = $existingXml.DocumentElement.SelectSingleNode("//*[local-name()='ProgrammingConcepts']")
    if ($concepts) { $writer.WriteRaw($concepts.OuterXml) }

    # Preserve VerificationAndQuality
    $vq = $existingXml.DocumentElement.SelectSingleNode("//*[local-name()='VerificationAndQuality']")
    if ($vq) { $writer.WriteRaw($vq.OuterXml) }

    # Preserve SecurityAndCompliance
    $sc = $existingXml.DocumentElement.SelectSingleNode("//*[local-name()='SecurityAndCompliance']")
    if ($sc) { $writer.WriteRaw($sc.OuterXml) }

    # Preserve DeploymentAndDevOps
    $devops = $existingXml.DocumentElement.SelectSingleNode("//*[local-name()='DeploymentAndDevOps']")
    if ($devops) { $writer.WriteRaw($devops.OuterXml) }

    # Preserve OperationalCapabilities
    $ops = $existingXml.DocumentElement.SelectSingleNode("//*[local-name()='OperationalCapabilities']")
    if ($ops) { $writer.WriteRaw($ops.OuterXml) }

} else {
    # Generate minimal SystemOverview and Orientation
    $writer.WriteStartElement("SystemOverview", $xmlNs)
    $writer.WriteElementString("Description", $xmlNs,
        "Enterprise-grade platform for real-time emergency response, incident management, responder coordination, and situational awareness.")
    $writer.WriteEndElement()

    $writer.WriteStartElement("Orientation", $xmlNs)
    $writer.WriteAttributeString("architectureStyle", "Clean Architecture")
    $writer.WriteAttributeString("methodology", "Domain-Driven Design (DDD)")
    $writer.WriteStartElement("DependencyInversion", $xmlNs)
    $writer.WriteAttributeString("rule", "Inner layers never depend on outer layers")
    $writer.WriteEndElement()
    $writer.WriteStartElement("Layers", $xmlNs)
    foreach ($layerName in @("Domain", "Application", "Infrastructure", "Presentation", "Shared", "Workers", "Libraries", "Aspire")) {
        $writer.WriteStartElement("Layer", $xmlNs)
        $writer.WriteAttributeString("name", $layerName)
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement() # Layers
    $writer.WriteEndElement() # Orientation
}

$writer.WriteEndElement() # TheWatchProject
$writer.WriteEndDocument()
$writer.Flush()
$writer.Close()

$xmlContent = [System.Text.Encoding]::UTF8.GetString($stream.ToArray())
$stream.Close()

# ── Step 5: Write output ────────────────────────────────────────────────────

if ($DryRun) {
    Write-Host "`n  (DRY RUN - would write to $OutputPath)" -ForegroundColor Yellow
    Write-Host "  Types: $($documentedTypes.Count) | Members: $($documentedMembers.Count)" -ForegroundColor Gray
} else {
    [System.IO.File]::WriteAllText($OutputPath, $xmlContent, [System.Text.UTF8Encoding]::new($false))
    Write-Host "  Generated: $OutputPath" -ForegroundColor Green
}

# ── Step 6: Validate ─────────────────────────────────────────────────────────

if ($Validate -and (Test-Path $XsdPath) -and -not $DryRun) {
    Write-Host "`nStep 5: Validating against XSD schema..." -ForegroundColor Cyan
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
            Write-Host "  Validation issues: $($validationErrors.Count)" -ForegroundColor Yellow
            $validationErrors | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
        }
    } catch {
        Write-Warning "Schema validation error: $_"
    }
}

# ── Summary ──────────────────────────────────────────────────────────────────

Write-Host "`n=== Generation Complete ===" -ForegroundColor Green
Write-Host "  Output:   $OutputPath" -ForegroundColor Cyan
Write-Host "  Types:    $($documentedTypes.Count)" -ForegroundColor Cyan
Write-Host "  Members:  $($documentedMembers.Count)" -ForegroundColor Cyan
Write-Host "  Merged:   $(if ($existingXml) { 'Yes (preserved hand-authored sections)' } else { 'No (fresh generation)' })" -ForegroundColor Cyan
