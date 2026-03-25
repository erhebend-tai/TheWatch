<#
.SYNOPSIS
Generates a Functions manifest XML from the codebase: handlers, endpoints, jobs, services, domain events.

.DESCRIPTION
Scans src/ for all behavioral elements and produces TheWatch.Functions.xml:
- MediatR command/query/notification handlers (IRequestHandler, INotificationHandler)
- Minimal API endpoints (MapGet/MapPost/MapPut/MapDelete/MapPatch)
- gRPC service implementations (inheriting from *.Base)
- SignalR hub classes (inheriting from Hub/Hub<T>)
- Background jobs (*Job classes)
- Hosted services (IHostedService, BackgroundService)
- Azure Functions ([Function] attribute)
- FluentValidation validators (AbstractValidator<T>)
- MediatR pipeline behaviors (IPipelineBehavior)
- Domain event classes

.PARAMETER OutputPath
Path to write the functions manifest. Default: ./XML/TheWatch.Functions.xml

.PARAMETER XsdPath
Path to the XSD schema for validation. Default: ./XML/TheWatchArchitecture.xsd

.PARAMETER Validate
Validate output against XSD after generation.

.PARAMETER DryRun
Show what would be generated without writing files.

.EXAMPLE
./generate-functions.ps1
./generate-functions.ps1 -Validate
./generate-functions.ps1 -DryRun
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
if (-not $OutputPath) { $OutputPath = Join-Path $RepoRoot "XML/TheWatch.Functions.xml" }
if (-not $XsdPath)    { $XsdPath    = Join-Path $RepoRoot "XML/TheWatchArchitecture.xsd" }

Write-Host "=== TheWatch Functions Generator ===" -ForegroundColor Green
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

function Get-LayerName {
    param([string]$FilePath)
    $rel = Get-RelativePath $FilePath
    $parts = $rel -split "[\\/]"
    if ($parts.Count -ge 1) { return $parts[0] }
    return "Unknown"
}

function Extract-Namespace {
    param([string[]]$Lines)
    foreach ($line in $Lines) {
        if ($line -match '^\s*namespace\s+([\w.]+)') { return $Matches[1] }
        if ($line -match '^\s*namespace\s+([\w.]+)\s*;') { return $Matches[1] }
    }
    return ""
}

function Extract-XmlSummary {
    param([string[]]$Lines, [int]$LineIndex)
    $summary = ""
    for ($i = $LineIndex - 1; $i -ge 0 -and $i -ge ($LineIndex - 5); $i--) {
        if ($Lines[$i] -match '///\s*<summary>') { break }
        if ($Lines[$i] -match '///\s*(.+)') {
            $text = $Matches[1] -replace '<[^>]+>', '' -replace '^\s+', ''
            if ($text -and $text -ne '[INSERT DESCRIPTION HERE]') {
                $summary = $text
            }
        }
    }
    return $summary
}

# ── Scan ─────────────────────────────────────────────────────────────────────

$csFiles = @(Get-ChildItem -Path $SrcPath -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]' })
Write-Host "Scanning $($csFiles.Count) C# files..." -ForegroundColor Cyan

$commandHandlers      = [System.Collections.ArrayList]::new()
$queryHandlers        = [System.Collections.ArrayList]::new()
$notificationHandlers = [System.Collections.ArrayList]::new()
$domainEventHandlers  = [System.Collections.ArrayList]::new()
$apiEndpoints         = [System.Collections.ArrayList]::new()
$grpcServices         = [System.Collections.ArrayList]::new()
$signalrHubs          = [System.Collections.ArrayList]::new()
$backgroundJobs       = [System.Collections.ArrayList]::new()
$hostedServices       = [System.Collections.ArrayList]::new()
$azureFunctions       = [System.Collections.ArrayList]::new()
$validators           = [System.Collections.ArrayList]::new()
$pipelineBehaviors    = [System.Collections.ArrayList]::new()

foreach ($file in $csFiles) {
    try {
        $lines = @(Get-Content $file.FullName -Encoding UTF8 -ErrorAction SilentlyContinue)
        if ($lines.Count -eq 0) { continue }

        $ns = Extract-Namespace $lines
        $proj = Get-ProjectName $file.FullName
        $layer = Get-LayerName $file.FullName
        $rel = Get-RelativePath $file.FullName

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $summary = ""

            # ── MediatR Command Handlers ──
            if ($line -match 'class\s+(\w+)\s*.*:\s*.*IRequestHandler\s*<\s*(\w+)\s*,\s*(\w+)') {
                $summary = Extract-XmlSummary $lines $i
                [void]$commandHandlers.Add(@{
                    name = $Matches[1]; request = $Matches[2]; response = $Matches[3]
                    namespace = $ns; sourceFile = $rel; lineNumber = ($i + 1)
                    project = $proj; layer = $layer; summary = $summary
                })
            }
            elseif ($line -match 'class\s+(\w+)\s*.*:\s*.*IRequestHandler\s*<\s*(\w+)\s*>') {
                $summary = Extract-XmlSummary $lines $i
                [void]$commandHandlers.Add(@{
                    name = $Matches[1]; request = $Matches[2]; response = "Unit"
                    namespace = $ns; sourceFile = $rel; lineNumber = ($i + 1)
                    project = $proj; layer = $layer; summary = $summary
                })
            }

            # ── MediatR Notification Handlers ──
            if ($line -match 'class\s+(\w+)\s*.*:\s*.*INotificationHandler\s*<\s*(\w+)') {
                $summary = Extract-XmlSummary $lines $i
                $handlerName = $Matches[1]; $eventType = $Matches[2]
                # Domain event handlers vs regular notifications
                if ($eventType -match '(Event|Created|Updated|Deleted|Changed|Triggered|Detected|Initiated|Collected|Discovered|Ordered|Declared)') {
                    [void]$domainEventHandlers.Add(@{
                        name = $handlerName; request = $eventType; response = ""
                        namespace = $ns; sourceFile = $rel; lineNumber = ($i + 1)
                        project = $proj; layer = $layer; summary = $summary
                    })
                } else {
                    [void]$notificationHandlers.Add(@{
                        name = $handlerName; request = $eventType; response = ""
                        namespace = $ns; sourceFile = $rel; lineNumber = ($i + 1)
                        project = $proj; layer = $layer; summary = $summary
                    })
                }
            }

            # ── Query Handlers (convention: name contains Query) ──
            if ($line -match 'class\s+(\w*Query\w*Handler)\s*.*:\s*.*IRequestHandler') {
                # Already captured above as command handler; reclassify
                $qName = $Matches[1]
                $existing = $commandHandlers | Where-Object { $_.name -eq $qName }
                if ($existing) {
                    [void]$queryHandlers.Add($existing)
                    $commandHandlers.Remove($existing)
                }
            }

            # ── Minimal API Endpoints ──
            if ($line -match '\.(Map(?:Get|Post|Put|Delete|Patch))\s*\(\s*"([^"]+)"') {
                $method = $Matches[1] -replace 'Map', ''
                [void]$apiEndpoints.Add(@{
                    method = $method.ToUpper(); route = $Matches[2]
                    sourceFile = $rel; lineNumber = ($i + 1)
                    handler = ""; project = $proj
                })
            }

            # ── gRPC Services ──
            if ($line -match 'class\s+(\w+)\s*.*:\s*(\w+)\.(\w+Base)') {
                $summary = Extract-XmlSummary $lines $i
                [void]$grpcServices.Add(@{
                    name = $Matches[1]; base = "$($Matches[2]).$($Matches[3])"
                    namespace = $ns; sourceFile = $rel; project = $proj; summary = $summary
                })
            }

            # ── SignalR Hubs ──
            if ($line -match 'class\s+(\w+)\s*.*:\s*Hub\b') {
                $summary = Extract-XmlSummary $lines $i
                [void]$signalrHubs.Add(@{
                    name = $Matches[1]; namespace = $ns
                    sourceFile = $rel; lineNumber = ($i + 1); project = $proj; summary = $summary
                })
            }

            # ── Background Jobs ──
            if ($line -match 'class\s+(\w+Job)\b' -and $line -notmatch 'interface') {
                $summary = Extract-XmlSummary $lines $i
                [void]$backgroundJobs.Add(@{
                    name = $Matches[1]; namespace = $ns
                    sourceFile = $rel; lineNumber = ($i + 1); project = $proj; summary = $summary
                })
            }

            # ── Hosted Services / BackgroundService ──
            if ($line -match 'class\s+(\w+)\s*.*:\s*(BackgroundService|IHostedService)' -and $line -notmatch '\bJob\b') {
                $summary = Extract-XmlSummary $lines $i
                [void]$hostedServices.Add(@{
                    name = $Matches[1]; namespace = $ns
                    sourceFile = $rel; lineNumber = ($i + 1); project = $proj; summary = $summary
                })
            }

            # ── Azure Functions ──
            if ($line -match '\[Function\s*\(\s*"(\w+)"') {
                $summary = Extract-XmlSummary $lines $i
                # Look for trigger type in nearby lines
                $trigger = ""
                for ($j = $i; $j -lt [Math]::Min($i + 5, $lines.Count); $j++) {
                    if ($lines[$j] -match '\[(HttpTrigger|TimerTrigger|BlobTrigger|QueueTrigger|ServiceBusTrigger|EventHubTrigger|CosmosDBTrigger)') {
                        $trigger = $Matches[1]
                        break
                    }
                }
                [void]$azureFunctions.Add(@{
                    name = $Matches[1]; trigger = $trigger; namespace = $ns
                    sourceFile = $rel; lineNumber = ($i + 1); summary = $summary
                })
            }

            # ── FluentValidation Validators ──
            if ($line -match 'class\s+(\w+)\s*.*:\s*AbstractValidator\s*<\s*(\w+)') {
                [void]$validators.Add(@{
                    name = $Matches[1]; validates = $Matches[2]
                    namespace = $ns; sourceFile = $rel; lineNumber = ($i + 1)
                })
            }

            # ── Pipeline Behaviors ──
            if ($line -match 'class\s+(\w+)\s*.*:\s*IPipelineBehavior') {
                [void]$pipelineBehaviors.Add(@{
                    name = $Matches[1]; namespace = $ns
                    sourceFile = $rel; lineNumber = ($i + 1)
                })
            }
        }
    } catch {
        Write-Verbose "Error processing $($file.FullName): $_"
    }
}

# ── Summary ──────────────────────────────────────────────────────────────────

$total = $commandHandlers.Count + $queryHandlers.Count + $notificationHandlers.Count +
         $domainEventHandlers.Count + $apiEndpoints.Count + $grpcServices.Count +
         $signalrHubs.Count + $backgroundJobs.Count + $hostedServices.Count +
         $azureFunctions.Count + $validators.Count + $pipelineBehaviors.Count

Write-Host "`nDiscovered:" -ForegroundColor Green
Write-Host "  Command Handlers:      $($commandHandlers.Count)" -ForegroundColor Cyan
Write-Host "  Query Handlers:        $($queryHandlers.Count)" -ForegroundColor Cyan
Write-Host "  Notification Handlers: $($notificationHandlers.Count)" -ForegroundColor Cyan
Write-Host "  Domain Event Handlers: $($domainEventHandlers.Count)" -ForegroundColor Cyan
Write-Host "  API Endpoints:         $($apiEndpoints.Count)" -ForegroundColor Cyan
Write-Host "  gRPC Services:         $($grpcServices.Count)" -ForegroundColor Cyan
Write-Host "  SignalR Hubs:          $($signalrHubs.Count)" -ForegroundColor Cyan
Write-Host "  Background Jobs:       $($backgroundJobs.Count)" -ForegroundColor Cyan
Write-Host "  Hosted Services:       $($hostedServices.Count)" -ForegroundColor Cyan
Write-Host "  Azure Functions:       $($azureFunctions.Count)" -ForegroundColor Cyan
Write-Host "  Validators:            $($validators.Count)" -ForegroundColor Cyan
Write-Host "  Pipeline Behaviors:    $($pipelineBehaviors.Count)" -ForegroundColor Cyan
Write-Host "  ────────────────────────────" -ForegroundColor Gray
Write-Host "  Total:                 $total" -ForegroundColor Green

# ── Generate XML ─────────────────────────────────────────────────────────────

$ns = "http://thewatch.io/architecture/2026-03"
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.IndentChars = "  "
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$settings.OmitXmlDeclaration = $false

$stream = [System.IO.MemoryStream]::new()
$writer = [System.Xml.XmlWriter]::Create($stream, $settings)

$writer.WriteStartDocument()
$writer.WriteStartElement("FunctionsManifest", $ns)
$writer.WriteAttributeString("name", "The Watch - Functions Manifest")
$writer.WriteAttributeString("totalFunctions", $total.ToString())

# Metadata
$writer.WriteStartElement("Metadata", $ns)
$writer.WriteAttributeString("generator", "generate-functions.ps1")
$writer.WriteAttributeString("generatedAt", (Get-Date -Format "o"))
$writer.WriteAttributeString("framework", "net10.0")
$writer.WriteAttributeString("sourceRoot", $SrcPath)
$writer.WriteEndElement()

# Helper to write handler collection
function Write-HandlerCollection {
    param($Writer, $ElementName, $Items, $Ns)
    $Writer.WriteStartElement($ElementName, $Ns)
    $Writer.WriteAttributeString("count", $Items.Count.ToString())
    foreach ($item in $Items) {
        $Writer.WriteStartElement("Handler", $Ns)
        $Writer.WriteAttributeString("name", $item.name)
        if ($item.namespace)  { $Writer.WriteAttributeString("namespace", $item.namespace) }
        if ($item.sourceFile) { $Writer.WriteAttributeString("sourceFile", $item.sourceFile) }
        if ($item.lineNumber) { $Writer.WriteAttributeString("lineNumber", $item.lineNumber.ToString()) }
        if ($item.project)    { $Writer.WriteAttributeString("project", $item.project) }
        if ($item.layer)      { $Writer.WriteAttributeString("layer", $item.layer) }
        if ($item.request) {
            $Writer.WriteStartElement("Request", $Ns)
            $Writer.WriteAttributeString("type", $item.request)
            $Writer.WriteEndElement()
        }
        if ($item.response -and $item.response -ne "") {
            $Writer.WriteStartElement("Response", $Ns)
            $Writer.WriteAttributeString("type", $item.response)
            $Writer.WriteEndElement()
        }
        if ($item.summary) {
            $Writer.WriteElementString("Summary", $Ns, $item.summary)
        }
        $Writer.WriteEndElement()
    }
    $Writer.WriteEndElement()
}

Write-HandlerCollection $writer "CommandHandlers" $commandHandlers $ns
Write-HandlerCollection $writer "QueryHandlers" $queryHandlers $ns
Write-HandlerCollection $writer "NotificationHandlers" $notificationHandlers $ns
Write-HandlerCollection $writer "DomainEventHandlers" $domainEventHandlers $ns

# API Endpoints
$writer.WriteStartElement("ApiEndpoints", $ns)
$writer.WriteAttributeString("count", $apiEndpoints.Count.ToString())
# Group by project
$grouped = $apiEndpoints | Group-Object -Property project
foreach ($group in $grouped) {
    $writer.WriteStartElement("EndpointGroup", $ns)
    $writer.WriteAttributeString("name", $group.Name)
    foreach ($ep in $group.Group) {
        $writer.WriteStartElement("Endpoint", $ns)
        $writer.WriteAttributeString("method", $ep.method)
        $writer.WriteAttributeString("route", $ep.route)
        if ($ep.sourceFile) { $writer.WriteAttributeString("sourceFile", $ep.sourceFile) }
        if ($ep.lineNumber) { $writer.WriteAttributeString("lineNumber", $ep.lineNumber.ToString()) }
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# gRPC Services
$writer.WriteStartElement("GrpcServices", $ns)
$writer.WriteAttributeString("count", $grpcServices.Count.ToString())
foreach ($svc in $grpcServices) {
    $writer.WriteStartElement("GrpcService", $ns)
    $writer.WriteAttributeString("name", $svc.name)
    if ($svc.namespace)  { $writer.WriteAttributeString("namespace", $svc.namespace) }
    if ($svc.sourceFile) { $writer.WriteAttributeString("sourceFile", $svc.sourceFile) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# SignalR Hubs
$writer.WriteStartElement("SignalRHubs", $ns)
$writer.WriteAttributeString("count", $signalrHubs.Count.ToString())
foreach ($hub in $signalrHubs) {
    $writer.WriteStartElement("Hub", $ns)
    $writer.WriteAttributeString("name", $hub.name)
    if ($hub.namespace)  { $writer.WriteAttributeString("namespace", $hub.namespace) }
    if ($hub.sourceFile) { $writer.WriteAttributeString("sourceFile", $hub.sourceFile) }
    if ($hub.lineNumber) { $writer.WriteAttributeString("lineNumber", $hub.lineNumber.ToString()) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Background Jobs
$writer.WriteStartElement("BackgroundJobs", $ns)
$writer.WriteAttributeString("count", $backgroundJobs.Count.ToString())
foreach ($job in $backgroundJobs) {
    $writer.WriteStartElement("Job", $ns)
    $writer.WriteAttributeString("name", $job.name)
    if ($job.namespace)  { $writer.WriteAttributeString("namespace", $job.namespace) }
    if ($job.sourceFile) { $writer.WriteAttributeString("sourceFile", $job.sourceFile) }
    if ($job.lineNumber) { $writer.WriteAttributeString("lineNumber", $job.lineNumber.ToString()) }
    if ($job.project)    { $writer.WriteAttributeString("project", $job.project) }
    if ($job.summary) { $writer.WriteElementString("Summary", $ns, $job.summary) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Hosted Services
$writer.WriteStartElement("HostedServices", $ns)
$writer.WriteAttributeString("count", $hostedServices.Count.ToString())
foreach ($svc in $hostedServices) {
    $writer.WriteStartElement("HostedService", $ns)
    $writer.WriteAttributeString("name", $svc.name)
    if ($svc.namespace)  { $writer.WriteAttributeString("namespace", $svc.namespace) }
    if ($svc.sourceFile) { $writer.WriteAttributeString("sourceFile", $svc.sourceFile) }
    if ($svc.lineNumber) { $writer.WriteAttributeString("lineNumber", $svc.lineNumber.ToString()) }
    if ($svc.project)    { $writer.WriteAttributeString("project", $svc.project) }
    if ($svc.summary) { $writer.WriteElementString("Summary", $ns, $svc.summary) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Azure Functions
$writer.WriteStartElement("AzureFunctions", $ns)
$writer.WriteAttributeString("count", $azureFunctions.Count.ToString())
foreach ($fn in $azureFunctions) {
    $writer.WriteStartElement("Function", $ns)
    $writer.WriteAttributeString("name", $fn.name)
    if ($fn.trigger)     { $writer.WriteAttributeString("trigger", $fn.trigger) }
    if ($fn.namespace)   { $writer.WriteAttributeString("namespace", $fn.namespace) }
    if ($fn.sourceFile)  { $writer.WriteAttributeString("sourceFile", $fn.sourceFile) }
    if ($fn.lineNumber)  { $writer.WriteAttributeString("lineNumber", $fn.lineNumber.ToString()) }
    if ($fn.summary) { $writer.WriteElementString("Summary", $ns, $fn.summary) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Validators
$writer.WriteStartElement("Validators", $ns)
$writer.WriteAttributeString("count", $validators.Count.ToString())
foreach ($v in $validators) {
    $writer.WriteStartElement("Validator", $ns)
    $writer.WriteAttributeString("name", $v.name)
    $writer.WriteAttributeString("validates", $v.validates)
    if ($v.namespace)  { $writer.WriteAttributeString("namespace", $v.namespace) }
    if ($v.sourceFile) { $writer.WriteAttributeString("sourceFile", $v.sourceFile) }
    if ($v.lineNumber) { $writer.WriteAttributeString("lineNumber", $v.lineNumber.ToString()) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

# Pipeline Behaviors
$writer.WriteStartElement("PipelineBehaviors", $ns)
$writer.WriteAttributeString("count", $pipelineBehaviors.Count.ToString())
foreach ($b in $pipelineBehaviors) {
    $writer.WriteStartElement("Behavior", $ns)
    $writer.WriteAttributeString("name", $b.name)
    if ($b.namespace)  { $writer.WriteAttributeString("namespace", $b.namespace) }
    if ($b.sourceFile) { $writer.WriteAttributeString("sourceFile", $b.sourceFile) }
    if ($b.lineNumber) { $writer.WriteAttributeString("lineNumber", $b.lineNumber.ToString()) }
    $writer.WriteEndElement()
}
$writer.WriteEndElement()

$writer.WriteEndElement() # FunctionsManifest
$writer.WriteEndDocument()
$writer.Flush()
$writer.Close()

$xmlContent = [System.Text.Encoding]::UTF8.GetString($stream.ToArray())
$stream.Close()

if ($DryRun) {
    Write-Host "`n(DRY RUN) Would write to: $OutputPath" -ForegroundColor Yellow
    Write-Host $xmlContent.Substring(0, [Math]::Min(500, $xmlContent.Length)) -ForegroundColor Gray
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

Write-Host "`n=== Functions Generation Complete ===" -ForegroundColor Green
