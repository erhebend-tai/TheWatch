// =============================================================================
// TheWatch Production Monitoring — Azure Monitor Alert Rules
// =============================================================================
//
// Deploys Azure Monitor metric and log-based alert rules for production
// monitoring of the TheWatch emergency response platform.
//
// Alert categories:
//   - API health: 5xx rate, SOS endpoint latency
//   - Messaging: RabbitMQ queue depth
//   - Real-time: SignalR connection failures
//   - Container health: restart count, CPU, memory
//
// Usage:
//   az deployment group create \
//     --resource-group <rg-name> \
//     --template-file infra/monitoring.bicep \
//     --parameters infra/monitoring.parameters.json
//
// Dependencies:
//   - infra/main.bicep must be deployed first (provides Log Analytics, Container Apps)
//
// Write-ahead log:
//   - v1.0.0: Initial production monitoring rules (2026-03-24)
//   - Conservative thresholds chosen — alert early for life-safety system
//   - Action group sends email; expand to PagerDuty/SMS/webhook in v1.1
// =============================================================================

targetScope = 'resourceGroup'

// ── Parameters ─────────────────────────────────────────────────────────────

@description('Primary location matching main.bicep deployment.')
param primaryLocation string = 'eastus2'

@description('Base name prefix matching main.bicep.')
@minLength(3)
@maxLength(10)
param baseName string = 'thewatch'

@description('Email address for alert notifications. Replace with actual ops email before production deployment.')
param alertEmailAddress string = 'ops-alerts@thewatch.app'

@description('Tags applied to all monitoring resources.')
param tags object = {
  project: 'TheWatch'
  component: 'monitoring'
  managedBy: 'bicep'
}

// ── Variables ──────────────────────────────────────────────────────────────

var actionGroupName = '${baseName}-alerts-ag'
var logAnalyticsName = '${baseName}-logs'
var containerEnvName = '${baseName}-cae'
var apiContainerAppName = '${baseName}-api'

// ── References to existing resources ───────────────────────────────────────

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: logAnalyticsName
}

resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerEnvName
}

resource apiContainerApp 'Microsoft.App/containerApps@2024-03-01' existing = {
  name: apiContainerAppName
}

// ── Action Group — Email Notification ──────────────────────────────────────
// All alert rules route here. In production, add SMS receivers, webhook
// receivers (PagerDuty, OpsGenie), and Azure Function receivers for
// auto-remediation runbooks.
//
// Example — adding a webhook receiver:
//   webhookReceivers: [
//     { name: 'pagerduty', serviceUri: 'https://events.pagerduty.com/integration/...' }
//   ]

resource actionGroup 'Microsoft.Insights/actionGroups@2023-09-01-preview' = {
  name: actionGroupName
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'WatchAlerts'
    enabled: true
    emailReceivers: [
      {
        name: 'OpsTeamEmail'
        emailAddress: alertEmailAddress
        useCommonAlertSchema: true
      }
    ]
    // TODO: Add SMS receivers for critical alerts
    // smsReceivers: [
    //   {
    //     name: 'OpsTeamSMS'
    //     countryCode: '1'
    //     phoneNumber: '5551234567'
    //   }
    // ]
    // TODO: Add webhook receivers for PagerDuty / OpsGenie
    // webhookReceivers: [
    //   {
    //     name: 'PagerDuty'
    //     serviceUri: 'https://events.pagerduty.com/integration/<key>/enqueue'
    //     useCommonAlertSchema: true
    //   }
    // ]
  }
}

// ═══════════════════════════════════════════════════════════════════════════
// CRITICAL ALERTS — Require immediate human response
// ═══════════════════════════════════════════════════════════════════════════

// ── Alert 1: API 5xx Rate > 1% over 5 minutes ─────────────────────────────
// A sustained 5xx rate on a life-safety API means emergencies are not being
// processed. This is the highest-priority alert.
//
// Metric: Custom log query counting HTTP 5xx responses vs total requests.
// Threshold: > 1% of requests returning 5xx over a 5-minute window.

resource alert5xxRate 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${baseName}-api-5xx-rate-critical'
  location: primaryLocation
  tags: tags
  properties: {
    displayName: 'API 5xx Error Rate > 1% (Critical)'
    description: 'The API is returning server errors at a rate above 1% over the past 5 minutes. This directly impacts emergency response capability. Investigate immediately.'
    severity: 0 // Critical
    enabled: true
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    scopes: [
      logAnalytics.id
    ]
    criteria: {
      allOf: [
        {
          query: '''
            ContainerAppConsoleLogs_CL
            | where ContainerAppName_s == '${apiContainerAppName}'
            | where Log_s has 'HTTP' and Log_s has 'responded'
            | extend StatusCode = extract(@'responded (\d{3})', 1, Log_s)
            | summarize
                TotalRequests = count(),
                ServerErrors = countif(StatusCode startswith '5')
                by bin(TimeGenerated, 5m)
            | extend ErrorRate = round(toreal(ServerErrors) / toreal(TotalRequests) * 100, 2)
            | where ErrorRate > 1
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

// ── Alert 2: SOS Endpoint Latency p99 > 500ms ─────────────────────────────
// The SOS trigger endpoint is the most critical path in the entire system.
// If it takes more than 500ms at p99, responders are being notified late.
// In an emergency, every millisecond counts.

resource alertSosLatency 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${baseName}-sos-latency-p99-critical'
  location: primaryLocation
  tags: tags
  properties: {
    displayName: 'SOS Endpoint p99 Latency > 500ms (Critical)'
    description: 'The SOS trigger endpoint is responding slower than 500ms at the 99th percentile. Emergency notifications may be delayed. Investigate database, RabbitMQ, and SignalR connectivity immediately.'
    severity: 0 // Critical
    enabled: true
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    scopes: [
      logAnalytics.id
    ]
    criteria: {
      allOf: [
        {
          query: '''
            ContainerAppConsoleLogs_CL
            | where ContainerAppName_s == '${apiContainerAppName}'
            | where Log_s has '/api/response' and Log_s has 'responded'
            | extend ElapsedMs = todouble(extract(@'in (\d+\.?\d*)', 1, Log_s))
            | summarize P99Latency = percentile(ElapsedMs, 99) by bin(TimeGenerated, 5m)
            | where P99Latency > 500
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

// ── Alert 3: Container App Restart Count > 3 in 10 minutes ────────────────
// Frequent restarts indicate a crash loop. The API may be intermittently
// unavailable, causing dropped SOS requests and SignalR disconnects.

resource alertContainerRestarts 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${baseName}-container-restarts-critical'
  location: primaryLocation
  tags: tags
  properties: {
    displayName: 'Container App Restarts > 3 in 10min (Critical)'
    description: 'A container app has restarted more than 3 times in 10 minutes, indicating a crash loop. The API may be intermittently unavailable. Check container logs for OOM kills, unhandled exceptions, or failing health probes.'
    severity: 0 // Critical
    enabled: true
    evaluationFrequency: 'PT1M'
    windowSize: 'PT10M'
    scopes: [
      logAnalytics.id
    ]
    criteria: {
      allOf: [
        {
          query: '''
            ContainerAppSystemLogs_CL
            | where Reason_s == 'ContainerRestarted' or Reason_s has 'Restart'
            | where ContainerAppName_s startswith '${baseName}'
            | summarize RestartCount = count() by ContainerAppName_s, bin(TimeGenerated, 10m)
            | where RestartCount > 3
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

// ── Alert 4: Memory > 90% ──────────────────────────────────────────────────
// Memory pressure causes OOM kills and container restarts. At 90%, the
// container is one burst away from termination. Scale up or investigate leaks.

resource alertMemoryCritical 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${baseName}-memory-90pct-critical'
  location: primaryLocation
  tags: tags
  properties: {
    displayName: 'Memory Usage > 90% (Critical)'
    description: 'Container memory usage has exceeded 90%. The container is at high risk of OOM termination. Scale up container memory allocation or investigate memory leaks. Check for unbounded caches, large SignalR message buffers, or connection pool exhaustion.'
    severity: 0 // Critical
    enabled: true
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    scopes: [
      logAnalytics.id
    ]
    criteria: {
      allOf: [
        {
          query: '''
            ContainerAppConsoleLogs_CL
            | where ContainerAppName_s startswith '${baseName}'
            | join kind=inner (
                InsightsMetrics
                | where Namespace == 'container.azm.ms/memory'
                | where Name == 'workingSetBytes'
                | extend MemoryPct = Val / 1073741824 * 100
                | where MemoryPct > 90
            ) on $left.TimeGenerated == $right.TimeGenerated
            | summarize MaxMemoryPct = max(MemoryPct) by bin(TimeGenerated, 5m)
            | where MaxMemoryPct > 90
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

// ═══════════════════════════════════════════════════════════════════════════
// WARNING ALERTS — Require investigation within 30 minutes
// ═══════════════════════════════════════════════════════════════════════════

// ── Alert 5: RabbitMQ Queue Depth > 1000 ───────────────────────────────────
// Deep queues mean messages (SOS dispatches, responder notifications) are
// backing up. Consumers may be down or overwhelmed.
//
// Example scenario: Worker service crashes → dispatch messages pile up →
// responders never get notified.

resource alertRabbitQueueDepth 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${baseName}-rabbitmq-queue-depth-warning'
  location: primaryLocation
  tags: tags
  properties: {
    displayName: 'RabbitMQ Queue Depth > 1000 (Warning)'
    description: 'RabbitMQ queue depth has exceeded 1000 messages. Dispatch and notification messages may be backing up. Check worker service health and consumer connectivity. If queue is swarm-results, check the result processor.'
    severity: 2 // Warning
    enabled: true
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    scopes: [
      logAnalytics.id
    ]
    criteria: {
      allOf: [
        {
          query: '''
            ContainerAppConsoleLogs_CL
            | where ContainerAppName_s == 'rabbitmq'
            | where Log_s has 'queue' and Log_s has 'messages'
            | extend QueueDepth = toint(extract(@'messages[:\s]+(\d+)', 1, Log_s))
            | where QueueDepth > 1000
            | summarize MaxDepth = max(QueueDepth) by bin(TimeGenerated, 5m)
            | where MaxDepth > 1000
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

// ── Alert 6: SignalR Connection Failures > 10/min ──────────────────────────
// SignalR is the real-time channel for SOS alerts, sitreps, and responder
// tracking. Connection failures mean the dashboard and mobile clients are
// flying blind during an emergency.

resource alertSignalRFailures 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${baseName}-signalr-failures-warning'
  location: primaryLocation
  tags: tags
  properties: {
    displayName: 'SignalR Connection Failures > 10/min (Warning)'
    description: 'SignalR is experiencing more than 10 connection failures per minute. Real-time dashboard updates, SOS alerts, and responder tracking may be disrupted. Check Azure SignalR Service health, network connectivity, and CORS configuration.'
    severity: 2 // Warning
    enabled: true
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    scopes: [
      logAnalytics.id
    ]
    criteria: {
      allOf: [
        {
          query: '''
            ContainerAppConsoleLogs_CL
            | where ContainerAppName_s == '${apiContainerAppName}'
            | where Log_s has 'SignalR' and (Log_s has 'failed' or Log_s has 'error' or Log_s has 'disconnect')
            | summarize FailureCount = count() by bin(TimeGenerated, 1m)
            | where FailureCount > 10
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

// ── Alert 7: CPU > 80% Sustained 5 minutes ────────────────────────────────
// Sustained high CPU on a life-safety system means degraded response times.
// Scale out before it hits 100% and causes request timeouts.

resource alertCpuWarning 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${baseName}-cpu-80pct-warning'
  location: primaryLocation
  tags: tags
  properties: {
    displayName: 'CPU Usage > 80% Sustained 5min (Warning)'
    description: 'Container CPU usage has been above 80% for 5 minutes. API response times are likely degraded. Consider scaling out replicas or investigating CPU-intensive operations (AI inference calls, large query results, SignalR broadcast storms).'
    severity: 2 // Warning
    enabled: true
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    scopes: [
      logAnalytics.id
    ]
    criteria: {
      allOf: [
        {
          query: '''
            ContainerAppConsoleLogs_CL
            | where ContainerAppName_s startswith '${baseName}'
            | join kind=inner (
                InsightsMetrics
                | where Namespace == 'container.azm.ms/processor'
                | where Name == 'cpuUsageNanoCores'
                | extend CpuPct = Val / 1000000000 * 100
                | where CpuPct > 80
            ) on $left.TimeGenerated == $right.TimeGenerated
            | summarize AvgCpuPct = avg(CpuPct) by bin(TimeGenerated, 5m)
            | where AvgCpuPct > 80
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 5
            minFailingPeriodsToAlert: 5
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

// ── Outputs ────────────────────────────────────────────────────────────────

@description('Action group resource ID for use in additional alert rules.')
output actionGroupId string = actionGroup.id

@description('Action group name.')
output actionGroupName string = actionGroup.name
