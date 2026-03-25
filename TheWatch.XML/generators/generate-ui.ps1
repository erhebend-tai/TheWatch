<#
.SYNOPSIS
Generates a UI manifest XML from the codebase: XAML pages, Blazor components, MAUI controls, styles.

.DESCRIPTION
Scans src/ for all UI/presentation elements and produces TheWatch.UI.xml:
- XAML pages and content pages (.xaml files with code-behind)
- XAML custom controls and views
- Blazor .razor components and pages
- Blazor/MAUI component library contents
- Resource dictionaries and XAML styles
- CSS/SCSS stylesheets
- Static assets (wwwroot contents)
- Data bindings extracted from XAML

.PARAMETER OutputPath
Path to write the UI manifest. Default: ./XML/TheWatch.UI.xml

.PARAMETER XsdPath
Path to the XSD schema for validation. Default: ./XML/TheWatchArchitecture.xsd

.PARAMETER Validate
Validate output against XSD after generation.

.PARAMETER DryRun
Show what would be generated without writing files.

.EXAMPLE
./generate-ui.ps1
./generate-ui.ps1 -Validate
./generate-ui.ps1 -DryRun
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
if (-not $OutputPath) { $OutputPath = Join-Path $RepoRoot "XML/TheWatch.UI.xml" }
if (-not $XsdPath)    { $XsdPath    = Join-Path $RepoRoot "XML/TheWatchArchitecture.xsd" }

Write-Host "=== TheWatch UI Generator ===" -ForegroundColor Green
Write-Host "Source: $SrcPath" -ForegroundColor Gray

if (-not (Test-Path $SrcPath)) {
    Write-Error "Source directory not found: $SrcPath"
    exit 1
}

# ── Helpers ──────────────────────────────────────────────────────────────────

function Get-RelativePath { param([string]$Full) $Full -replace [regex]::Escape($SrcPath), "" -replace "^[\\/]", "" }

function Get-ProjectName {
    param([string]$FilePath)
    $rel = Get-RelativePath $FilePath
    $parts = $rel -split "[\\/]"
    if ($parts.Count -ge 2) { return $parts[1] }
    return "Unknown"
}

# ── Scan XAML ────────────────────────────────────────────────────────────────

Write-Host "`nScanning XAML files..." -ForegroundColor Cyan

$xamlFiles = @(Get-ChildItem -Path $SrcPath -Filter "*.xaml" -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]' })

$xamlPages    = [System.Collections.ArrayList]::new()
$xamlControls = [System.Collections.ArrayList]::new()
$resourceDicts = [System.Collections.ArrayList]::new()

foreach ($file in $xamlFiles) {
    try {
        $content = Get-Content $file.FullName -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        if (-not $content) { continue }

        $rel = Get-RelativePath $file.FullName
        $proj = Get-ProjectName $file.FullName
        $name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $codeBehind = "$($file.FullName).cs"
        $hasCodeBehind = Test-Path $codeBehind

        # Determine page type from root element
        $pageType = ""
        if ($content -match '<ContentPage\b')     { $pageType = "ContentPage" }
        elseif ($content -match '<FlyoutPage\b')   { $pageType = "FlyoutPage" }
        elseif ($content -match '<TabbedPage\b')   { $pageType = "TabbedPage" }
        elseif ($content -match '<NavigationPage\b') { $pageType = "NavigationPage" }
        elseif ($content -match '<Shell\b')        { $pageType = "Shell" }

        # Extract namespace
        $namespace = ""
        if ($content -match 'x:Class="([^"]+)"') { $namespace = $Matches[1] }

        # Extract ViewModel binding
        $viewModel = ""
        if ($content -match 'BindingContext.*?=.*?(\w+ViewModel)') { $viewModel = $Matches[1] }
        if ($content -match 'x:DataType="[^"]*\.(\w+ViewModel)"') { $viewModel = $Matches[1] }

        # Extract data bindings
        $bindings = [System.Collections.ArrayList]::new()
        $bindingMatches = [regex]::Matches($content, '\{Binding\s+(?:Path=)?(\w[\w.]*)')
        foreach ($m in $bindingMatches) {
            [void]$bindings.Add(@{ property = ""; path = $m.Groups[1].Value; mode = "" })
        }
        # Also x:Bind syntax
        $xbindMatches = [regex]::Matches($content, '\{x:Bind\s+(\w[\w.]*)')
        foreach ($m in $xbindMatches) {
            [void]$bindings.Add(@{ property = ""; path = $m.Groups[1].Value; mode = "x:Bind" })
        }

        # Classify: page vs control vs resource dictionary
        if ($content -match '<ResourceDictionary\b' -and $content -notmatch '<ContentPage') {
            [void]$resourceDicts.Add(@{
                name = $name; xamlFile = $rel; project = $proj; scope = "Application"
            })
        }
        elseif ($pageType) {
            [void]$xamlPages.Add(@{
                name = $name; xamlFile = $rel
                codeBehind = if ($hasCodeBehind) { "$rel.cs" } else { "" }
                pageType = $pageType; project = $proj; namespace = $namespace
                viewModel = $viewModel; bindings = $bindings
            })
        }
        else {
            # ContentView, custom controls, etc.
            $baseType = ""
            if ($content -match '<ContentView\b')  { $baseType = "ContentView" }
            elseif ($content -match '<Grid\b')      { $baseType = "Grid" }
            elseif ($content -match '<StackLayout\b') { $baseType = "StackLayout" }
            elseif ($content -match '<Frame\b')     { $baseType = "Frame" }
            [void]$xamlControls.Add(@{
                name = $name; xamlFile = $rel
                codeBehind = if ($hasCodeBehind) { "$rel.cs" } else { "" }
                baseType = $baseType; project = $proj
            })
        }
    } catch {
        Write-Verbose "Error processing XAML $($file.FullName): $_"
    }
}

Write-Host "  XAML Pages:    $($xamlPages.Count)" -ForegroundColor Cyan
Write-Host "  XAML Controls: $($xamlControls.Count)" -ForegroundColor Cyan
Write-Host "  Resource Dicts: $($resourceDicts.Count)" -ForegroundColor Cyan

# ── Scan Blazor Components ───────────────────────────────────────────────────

Write-Host "`nScanning Blazor .razor files..." -ForegroundColor Cyan

$razorFiles = @(Get-ChildItem -Path $SrcPath -Filter "*.razor" -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]' })

$blazorComponents = [System.Collections.ArrayList]::new()
$blazorPages      = [System.Collections.ArrayList]::new()

foreach ($file in $razorFiles) {
    try {
        $content = Get-Content $file.FullName -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        if (-not $content) { continue }

        $rel = Get-RelativePath $file.FullName
        $proj = Get-ProjectName $file.FullName
        $name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $codeBehind = "$($file.FullName).cs"
        $hasCodeBehind = Test-Path $codeBehind

        # Check for @page directive (makes it a routable page)
        $route = ""
        if ($content -match '@page\s+"([^"]+)"') { $route = $Matches[1] }

        # Check for @layout
        $layout = ""
        if ($content -match '@layout\s+(\w+)') { $layout = $Matches[1] }

        # Check for @inherits
        $inherits = ""
        if ($content -match '@inherits\s+([\w.]+)') { $inherits = $Matches[1] }

        # Extract @namespace
        $namespace = ""
        if ($content -match '@namespace\s+([\w.]+)') { $namespace = $Matches[1] }

        # Extract [Parameter] properties from code-behind or @code block
        $parameters = [System.Collections.ArrayList]::new()
        if ($content -match '@code\s*\{') {
            $paramMatches = [regex]::Matches($content, '\[Parameter\].*?public\s+([\w<>?,\s]+)\s+(\w+)')
            foreach ($m in $paramMatches) {
                [void]$parameters.Add(@{ name = $m.Groups[2].Value; type = $m.Groups[1].Value.Trim() })
            }
        }
        if ($hasCodeBehind) {
            try {
                $cbContent = Get-Content $codeBehind -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
                if ($cbContent) {
                    $paramMatches = [regex]::Matches($cbContent, '\[Parameter\].*?public\s+([\w<>?,\s]+)\s+(\w+)')
                    foreach ($m in $paramMatches) {
                        [void]$parameters.Add(@{ name = $m.Groups[2].Value; type = $m.Groups[1].Value.Trim() })
                    }
                }
            } catch { }
        }

        if ($route) {
            [void]$blazorPages.Add(@{
                name = $name; route = $route; razorFile = $rel
                layout = $layout; project = $proj
            })
        } else {
            [void]$blazorComponents.Add(@{
                name = $name; razorFile = $rel
                codeBehind = if ($hasCodeBehind) { "$rel.cs" } else { "" }
                project = $proj; namespace = $namespace; inherits = $inherits
                parameters = $parameters
            })
        }
    } catch {
        Write-Verbose "Error processing Razor $($file.FullName): $_"
    }
}

Write-Host "  Blazor Pages:      $($blazorPages.Count)" -ForegroundColor Cyan
Write-Host "  Blazor Components: $($blazorComponents.Count)" -ForegroundColor Cyan

# ── Scan Component Libraries ─────────────────────────────────────────────────

Write-Host "`nScanning component libraries..." -ForegroundColor Cyan

$componentLibraries = [System.Collections.ArrayList]::new()
$libPath = Join-Path $SrcPath "Libraries"

if (Test-Path $libPath) {
    $libProjects = @(Get-ChildItem -Path $libPath -Filter "*.csproj" -Recurse -ErrorAction SilentlyContinue)
    foreach ($csproj in $libProjects) {
        $libDir = $csproj.DirectoryName
        $libName = [System.IO.Path]::GetFileNameWithoutExtension($csproj.Name)
        $razorCount = @(Get-ChildItem -Path $libDir -Filter "*.razor" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }).Count
        $xamlCount = @(Get-ChildItem -Path $libDir -Filter "*.xaml" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }).Count

        # Determine technology
        $csprojContent = Get-Content $csproj.FullName -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        $tech = ""
        if ($csprojContent -match 'Microsoft\.NET\.Sdk\.Razor') { $tech = "Blazor" }
        elseif ($csprojContent -match 'Maui|Microsoft\.Maui') { $tech = "MAUI" }
        elseif ($csprojContent -match 'Microsoft\.NET\.Sdk') { $tech = ".NET" }

        [void]$componentLibraries.Add(@{
            name = $libName; technology = $tech
            componentCount = $razorCount + $xamlCount
            project = $libName
        })
    }
}

Write-Host "  Component Libraries: $($componentLibraries.Count)" -ForegroundColor Cyan

# ── Scan Styles ──────────────────────────────────────────────────────────────

Write-Host "`nScanning stylesheets..." -ForegroundColor Cyan

$styles = [System.Collections.ArrayList]::new()
$styleExts = @("*.css", "*.scss", "*.razor.css")
foreach ($ext in $styleExts) {
    $styleFiles = @(Get-ChildItem -Path $SrcPath -Filter $ext -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]' })
    foreach ($file in $styleFiles) {
        $kind = switch -Regex ($file.Extension) {
            '\.scss$'      { "scss" }
            '\.razor\.css' { "razor.css" }
            default        { "css" }
        }
        [void]$styles.Add(@{
            name = $file.Name; path = (Get-RelativePath $file.FullName)
            type = $kind; project = (Get-ProjectName $file.FullName)
        })
    }
}

Write-Host "  Stylesheets: $($styles.Count)" -ForegroundColor Cyan

# ── Scan Static Assets ───────────────────────────────────────────────────────

Write-Host "`nScanning static assets (wwwroot)..." -ForegroundColor Cyan

$staticAssets = [System.Collections.ArrayList]::new()
$wwwrootDirs = @(Get-ChildItem -Path $SrcPath -Directory -Filter "wwwroot" -Recurse -ErrorAction SilentlyContinue)
foreach ($wwwroot in $wwwrootDirs) {
    $assets = @(Get-ChildItem -Path $wwwroot.FullName -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -match '\.(js|css|png|jpg|jpeg|svg|ico|woff|woff2|ttf|json)$' })
    foreach ($asset in $assets) {
        [void]$staticAssets.Add(@{
            name = $asset.Name; path = (Get-RelativePath $asset.FullName)
            type = $asset.Extension.TrimStart('.'); sizeBytes = $asset.Length
        })
    }
}

Write-Host "  Static Assets: $($staticAssets.Count)" -ForegroundColor Cyan

# ── Totals ───────────────────────────────────────────────────────────────────

$totalComponents = $xamlPages.Count + $xamlControls.Count + $blazorComponents.Count +
                   $blazorPages.Count + $componentLibraries.Count + $styles.Count +
                   $staticAssets.Count + $resourceDicts.Count

Write-Host "`n  Total UI elements: $totalComponents" -ForegroundColor Green

# ── Generate XML ─────────────────────────────────────────────────────────────

$xmlNs = "http://thewatch.io/architecture/2026-03"
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.IndentChars = "  "
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)

$stream = [System.IO.MemoryStream]::new()
$writer = [System.Xml.XmlWriter]::Create($stream, $settings)

$writer.WriteStartDocument()
$writer.WriteStartElement("UIManifest", $xmlNs)
$writer.WriteAttributeString("name", "The Watch - UI Manifest")
$writer.WriteAttributeString("totalComponents", $totalComponents.ToString())

# Metadata
$writer.WriteStartElement("Metadata", $xmlNs)
$writer.WriteAttributeString("generator", "generate-ui.ps1")
$writer.WriteAttributeString("generatedAt", (Get-Date -Format "o"))
$writer.WriteAttributeString("framework", "net10.0")
$writer.WriteAttributeString("sourceRoot", $SrcPath)
$writer.WriteEndElement()

# XAML Pages
$writer.WriteStartElement("XamlPages", $xmlNs)
$writer.WriteAttributeString("count", $xamlPages.Count.ToString())
foreach ($page in $xamlPages) {
    $writer.WriteStartElement("Page", $xmlNs)
    $writer.WriteAttributeString("name", $page.name)
    if ($page.xamlFile)  { $writer.WriteAttributeString("xamlFile", $page.xamlFile) }
    if ($page.codeBehind) { $writer.WriteAttributeString("codeBehind", $page.codeBehind) }
    if ($page.pageType)  { $writer.WriteAttributeString("pageType", $page.pageType) }
    if ($page.project)   { $writer.WriteAttributeString("project", $page.project) }
    if ($page.namespace) { $writer.WriteAttributeString("namespace", $page.namespace) }
    if ($page.viewModel) { $writer.WriteAttributeString("viewModel", $page.viewModel) }
    if ($page.bindings -and $page.bindings.Count -gt 0) {
        $writer.WriteStartElement("Bindings", $xmlNs)
        foreach ($b in $page.bindings) {
            $writer.WriteStartElement("Binding", $xmlNs)
            $writer.WriteAttributeString("path", $b.path)
            if ($b.mode) { $writer.WriteAttributeString("mode", $b.mode) }
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# XAML Controls
$writer.WriteStartElement("XamlControls", $xmlNs)
$writer.WriteAttributeString("count", $xamlControls.Count.ToString())
foreach ($ctrl in $xamlControls) {
    $writer.WriteStartElement("Control", $xmlNs)
    $writer.WriteAttributeString("name", $ctrl.name)
    if ($ctrl.xamlFile)   { $writer.WriteAttributeString("xamlFile", $ctrl.xamlFile) }
    if ($ctrl.codeBehind) { $writer.WriteAttributeString("codeBehind", $ctrl.codeBehind) }
    if ($ctrl.baseType)   { $writer.WriteAttributeString("baseType", $ctrl.baseType) }
    if ($ctrl.project)    { $writer.WriteAttributeString("project", $ctrl.project) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Blazor Components
$writer.WriteStartElement("BlazorComponents", $xmlNs)
$writer.WriteAttributeString("count", $blazorComponents.Count.ToString())
foreach ($comp in $blazorComponents) {
    $writer.WriteStartElement("Component", $xmlNs)
    $writer.WriteAttributeString("name", $comp.name)
    if ($comp.razorFile)  { $writer.WriteAttributeString("razorFile", $comp.razorFile) }
    if ($comp.codeBehind) { $writer.WriteAttributeString("codeBehind", $comp.codeBehind) }
    if ($comp.project)    { $writer.WriteAttributeString("project", $comp.project) }
    if ($comp.namespace)  { $writer.WriteAttributeString("namespace", $comp.namespace) }
    if ($comp.inherits)   { $writer.WriteAttributeString("inherits", $comp.inherits) }
    if ($comp.parameters -and $comp.parameters.Count -gt 0) {
        foreach ($p in $comp.parameters) {
            $writer.WriteStartElement("Parameter", $xmlNs)
            $writer.WriteAttributeString("name", $p.name)
            if ($p.type) { $writer.WriteAttributeString("type", $p.type) }
            $writer.WriteEndElement()
        }
    }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Blazor Pages
$writer.WriteStartElement("BlazorPages", $xmlNs)
$writer.WriteAttributeString("count", $blazorPages.Count.ToString())
foreach ($page in $blazorPages) {
    $writer.WriteStartElement("Page", $xmlNs)
    $writer.WriteAttributeString("name", $page.name)
    if ($page.route)     { $writer.WriteAttributeString("route", $page.route) }
    if ($page.razorFile) { $writer.WriteAttributeString("razorFile", $page.razorFile) }
    if ($page.layout)    { $writer.WriteAttributeString("layout", $page.layout) }
    if ($page.project)   { $writer.WriteAttributeString("project", $page.project) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Component Libraries
$writer.WriteStartElement("ComponentLibraries", $xmlNs)
$writer.WriteAttributeString("count", $componentLibraries.Count.ToString())
foreach ($lib in $componentLibraries) {
    $writer.WriteStartElement("Library", $xmlNs)
    $writer.WriteAttributeString("name", $lib.name)
    if ($lib.technology)     { $writer.WriteAttributeString("technology", $lib.technology) }
    if ($lib.componentCount) { $writer.WriteAttributeString("componentCount", $lib.componentCount.ToString()) }
    if ($lib.project)        { $writer.WriteAttributeString("project", $lib.project) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Styles
$writer.WriteStartElement("Styles", $xmlNs)
$writer.WriteAttributeString("count", $styles.Count.ToString())
foreach ($s in $styles) {
    $writer.WriteStartElement("StyleFile", $xmlNs)
    $writer.WriteAttributeString("name", $s.name)
    if ($s.path)    { $writer.WriteAttributeString("path", $s.path) }
    if ($s.type)    { $writer.WriteAttributeString("type", $s.type) }
    if ($s.project) { $writer.WriteAttributeString("project", $s.project) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Static Assets
$writer.WriteStartElement("StaticAssets", $xmlNs)
$writer.WriteAttributeString("count", $staticAssets.Count.ToString())
foreach ($a in $staticAssets) {
    $writer.WriteStartElement("Asset", $xmlNs)
    $writer.WriteAttributeString("name", $a.name)
    if ($a.path)      { $writer.WriteAttributeString("path", $a.path) }
    if ($a.type)      { $writer.WriteAttributeString("type", $a.type) }
    if ($a.sizeBytes) { $writer.WriteAttributeString("sizeBytes", $a.sizeBytes.ToString()) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Resource Dictionaries
$writer.WriteStartElement("ResourceDictionaries", $xmlNs)
$writer.WriteAttributeString("count", $resourceDicts.Count.ToString())
foreach ($rd in $resourceDicts) {
    $writer.WriteStartElement("ResourceDictionary", $xmlNs)
    $writer.WriteAttributeString("name", $rd.name)
    if ($rd.xamlFile) { $writer.WriteAttributeString("xamlFile", $rd.xamlFile) }
    if ($rd.project)  { $writer.WriteAttributeString("project", $rd.project) }
    if ($rd.scope)    { $writer.WriteAttributeString("scope", $rd.scope) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

$writer.WriteEndElement() # UIManifest
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

Write-Host "`n=== UI Generation Complete ===" -ForegroundColor Green
