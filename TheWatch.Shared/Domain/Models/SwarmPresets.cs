// SwarmPresets — pre-built swarm topologies for common TheWatch workflows.
// Each preset returns a fully configured SwarmDefinition ready to register.
//
// Example:
//   var swarm = SwarmPresets.SafetyReportPipeline();
//   await swarmPort.CreateSwarmAsync(swarm, ct);
//
// Available presets:
//   SafetyReportPipeline  — SOS triage → evidence analysis → location → responder dispatch → review
//   CodeReviewSwarm       — triage → security → architecture → style → aggregator
//   IncidentResponseSwarm — classify → assess → coordinate → communicate → close
//   StandardsAuditSwarm   — ingest → map requirements → gap analysis → remediation plan
//   EmergencyDispatchSwarm — intake → threat assessment → resource allocation → notification → supervisor

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public static class SwarmPresets
{
    /// <summary>
    /// Safety report pipeline — the core TheWatch emergency workflow.
    /// SOS trigger → triage → evidence analysis → location resolution → responder dispatch → review.
    /// </summary>
    public static SwarmDefinition SafetyReportPipeline() => new()
    {
        SwarmId = "safety-report-pipeline",
        Name = "Safety Report Pipeline",
        Description = "Multi-agent pipeline for processing SOS triggers, analyzing evidence, resolving locations, dispatching responders, and reviewing outcomes.",
        EntryPointAgentId = "triage",
        MaxConcurrentTasks = 10,
        MaxHandoffDepth = 8,
        Agents =
        [
            new SwarmAgentDefinition
            {
                AgentId = "triage",
                Name = "Triage Agent",
                Role = SwarmRole.Triage,
                IsEntryPoint = true,
                Instructions = """
                    You are the triage agent for TheWatch safety system.
                    Classify incoming safety reports by severity and type.

                    Categories:
                    - SOS_EMERGENCY: Immediate danger, phrase detection, duress signals
                    - CHECK_IN_REQUEST: Periodic safety verification
                    - EVIDENCE_SUBMISSION: Photos, video, text reports from responders
                    - LOCATION_ALERT: Geofence breach, unusual movement patterns
                    - DEVICE_ALERT: IoT sensor triggers (smoke, glass break, motion)

                    After classification, hand off to the appropriate specialist:
                    - SOS_EMERGENCY or DEVICE_ALERT → threat-assessor
                    - CHECK_IN_REQUEST → check-in-handler
                    - EVIDENCE_SUBMISSION → evidence-analyst
                    - LOCATION_ALERT → location-resolver

                    Always include the original report text and your classification in the handoff.
                    """,
                HandoffTargets = ["threat-assessor", "evidence-analyst", "location-resolver", "check-in-handler"],
                Tools =
                [
                    new SwarmToolDefinition
                    {
                        Name = "classify_report",
                        Description = "Classify an incoming safety report into a category and severity level",
                        ParametersJson = """
                        {
                            "type": "object",
                            "properties": {
                                "category": { "type": "string", "enum": ["SOS_EMERGENCY", "CHECK_IN_REQUEST", "EVIDENCE_SUBMISSION", "LOCATION_ALERT", "DEVICE_ALERT"] },
                                "severity": { "type": "integer", "minimum": 1, "maximum": 5 },
                                "summary": { "type": "string" }
                            },
                            "required": ["category", "severity", "summary"]
                        }
                        """
                    }
                ]
            },
            new SwarmAgentDefinition
            {
                AgentId = "threat-assessor",
                Name = "Threat Assessment Agent",
                Role = SwarmRole.Specialist,
                Instructions = """
                    You assess the threat level of emergency reports.
                    Determine:
                    1. Is this a genuine emergency or a false alarm?
                    2. What is the immediate danger level (1-5)?
                    3. Are first responders needed (police, fire, EMS)?
                    4. What resources should be dispatched?

                    After assessment, hand off to responder-dispatch with your findings.
                    If evidence needs analysis first, hand off to evidence-analyst.
                    """,
                HandoffTargets = ["responder-dispatch", "evidence-analyst"],
                Tools =
                [
                    new SwarmToolDefinition
                    {
                        Name = "assess_threat",
                        Description = "Evaluate threat level and determine response requirements",
                        ParametersJson = """
                        {
                            "type": "object",
                            "properties": {
                                "threat_level": { "type": "integer", "minimum": 1, "maximum": 5 },
                                "is_genuine": { "type": "boolean" },
                                "requires_911": { "type": "boolean" },
                                "recommended_responders": { "type": "integer" },
                                "assessment_notes": { "type": "string" }
                            },
                            "required": ["threat_level", "is_genuine", "requires_911"]
                        }
                        """
                    },
                    new SwarmToolDefinition
                    {
                        Name = "get_user_history",
                        Description = "Retrieve the user's alert history to check for patterns",
                        ParametersJson = """
                        {
                            "type": "object",
                            "properties": {
                                "user_id": { "type": "string" }
                            },
                            "required": ["user_id"]
                        }
                        """
                    }
                ]
            },
            new SwarmAgentDefinition
            {
                AgentId = "evidence-analyst",
                Name = "Evidence Analysis Agent",
                Role = SwarmRole.Specialist,
                Instructions = """
                    You analyze evidence submissions (text descriptions, metadata about images/video/audio).
                    Extract key facts: who, what, where, when, visible threats, injuries, vehicle descriptions, etc.
                    Summarize findings and hand off to the reviewer.
                    If location data is ambiguous, hand off to location-resolver first.
                    """,
                HandoffTargets = ["reviewer", "location-resolver"],
                Tools =
                [
                    new SwarmToolDefinition
                    {
                        Name = "extract_evidence_facts",
                        Description = "Extract structured facts from evidence submission text",
                        ParametersJson = """
                        {
                            "type": "object",
                            "properties": {
                                "persons_described": { "type": "array", "items": { "type": "string" } },
                                "threats_identified": { "type": "array", "items": { "type": "string" } },
                                "location_clues": { "type": "array", "items": { "type": "string" } },
                                "timestamp_clues": { "type": "array", "items": { "type": "string" } },
                                "urgency_indicators": { "type": "array", "items": { "type": "string" } }
                            },
                            "required": ["persons_described", "threats_identified"]
                        }
                        """
                    }
                ]
            },
            new SwarmAgentDefinition
            {
                AgentId = "location-resolver",
                Name = "Location Resolution Agent",
                Role = SwarmRole.Specialist,
                Instructions = """
                    You resolve and enrich location data for safety reports.
                    Given coordinates, addresses, or location descriptions:
                    1. Normalize to lat/lng coordinates
                    2. Reverse geocode to human-readable address
                    3. Identify nearby landmarks, building types, floor levels
                    4. Determine jurisdiction (police district, fire station, hospital proximity)
                    5. Calculate proximity to registered volunteers/responders via geohash/H3

                    Hand off to responder-dispatch with enriched location data.
                    """,
                HandoffTargets = ["responder-dispatch"],
                Tools =
                [
                    new SwarmToolDefinition
                    {
                        Name = "geocode_location",
                        Description = "Convert address or description to coordinates, or reverse geocode coords to address",
                        ParametersJson = """
                        {
                            "type": "object",
                            "properties": {
                                "latitude": { "type": "number" },
                                "longitude": { "type": "number" },
                                "address": { "type": "string" },
                                "description": { "type": "string" }
                            }
                        }
                        """
                    },
                    new SwarmToolDefinition
                    {
                        Name = "find_nearby_responders",
                        Description = "Find volunteer responders within radius using geohash/H3 index",
                        ParametersJson = """
                        {
                            "type": "object",
                            "properties": {
                                "latitude": { "type": "number" },
                                "longitude": { "type": "number" },
                                "radius_km": { "type": "number", "default": 5 },
                                "min_responders": { "type": "integer", "default": 5 }
                            },
                            "required": ["latitude", "longitude"]
                        }
                        """
                    }
                ]
            },
            new SwarmAgentDefinition
            {
                AgentId = "check-in-handler",
                Name = "Check-In Handler Agent",
                Role = SwarmRole.Specialist,
                Instructions = """
                    You handle periodic check-in requests.
                    1. Verify the user's identity and check-in schedule
                    2. Record the check-in with timestamp and location
                    3. If the check-in is overdue, escalate to threat-assessor
                    4. If the user reports distress, hand off to threat-assessor
                    5. Otherwise, confirm the check-in and produce a summary
                    """,
                HandoffTargets = ["threat-assessor", "reviewer"],
                Tools =
                [
                    new SwarmToolDefinition
                    {
                        Name = "record_check_in",
                        Description = "Record a user check-in with status",
                        ParametersJson = """
                        {
                            "type": "object",
                            "properties": {
                                "user_id": { "type": "string" },
                                "status": { "type": "string", "enum": ["ok", "distressed", "overdue", "no_response"] },
                                "notes": { "type": "string" }
                            },
                            "required": ["user_id", "status"]
                        }
                        """
                    }
                ]
            },
            new SwarmAgentDefinition
            {
                AgentId = "responder-dispatch",
                Name = "Responder Dispatch Agent",
                Role = SwarmRole.Specialist,
                Instructions = """
                    You coordinate responder dispatch based on threat assessment and location data.
                    1. Select appropriate dispatch strategy (NearestN, RadiusBroadcast, TrustedContactsOnly, etc.)
                    2. Determine responder count based on severity
                    3. Issue dispatch notifications via dispatch_responders tool
                    4. If 911 is required (threat level >= 4, or requires_911 was true from threat assessment),
                       ALWAYS call the initiate_911_call tool to notify emergency services on behalf of the user.
                       This triggers a Twilio voice call to the local PSAP with TTS context summary,
                       and pushes GPS coordinates to RapidSOS for the dispatcher's map.
                    5. Hand off to reviewer with dispatch summary including whether 911 was contacted

                    CRITICAL: When the threat assessment says requires_911=true, you MUST call initiate_911_call.
                    The user has opted in to automatic 911 notification at signup. Failing to call 911 when
                    required could result in delayed emergency response.
                    """,
                HandoffTargets = ["reviewer"],
                Tools =
                [
                    new SwarmToolDefinition
                    {
                        Name = "dispatch_responders",
                        Description = "Dispatch volunteer responders and/or first responders to the location",
                        ParametersJson = """
                        {
                            "type": "object",
                            "properties": {
                                "strategy": { "type": "string", "enum": ["NearestN", "RadiusBroadcast", "TrustedContactsOnly", "CertifiedFirst", "EmergencyBroadcast"] },
                                "responder_count": { "type": "integer" },
                                "notify_911": { "type": "boolean" },
                                "latitude": { "type": "number" },
                                "longitude": { "type": "number" },
                                "urgency": { "type": "string", "enum": ["low", "medium", "high", "critical"] }
                            },
                            "required": ["strategy", "responder_count", "notify_911"]
                        }
                        """
                    },
                    new SwarmToolDefinition
                    {
                        Name = "initiate_911_call",
                        Description = "Initiate a 911 emergency call on behalf of the user via Twilio/RapidSOS. Places a voice call to the local PSAP with TTS context and pushes GPS to the dispatcher's map. Only call when threat assessment indicates 911 is needed.",
                        ParametersJson = """
                        {
                            "type": "object",
                            "properties": {
                                "service_type": { "type": "string", "enum": ["Police", "Fire", "Ems", "All"], "description": "Which emergency service to request" },
                                "latitude": { "type": "number", "description": "User's GPS latitude" },
                                "longitude": { "type": "number", "description": "User's GPS longitude" },
                                "context_summary": { "type": "string", "description": "Brief summary for the 911 dispatcher TTS relay (what happened, who needs help, any injuries)" },
                                "user_name": { "type": "string", "description": "User's name for the dispatcher" },
                                "user_phone": { "type": "string", "description": "User's callback number" },
                                "medical_info": { "type": "string", "description": "Relevant medical conditions, allergies, medications" },
                                "volunteers_en_route": { "type": "integer", "description": "How many volunteer responders are already dispatched" },
                                "access_instructions": { "type": "string", "description": "Gate code, lock box, floor number, or other access info for first responders" }
                            },
                            "required": ["service_type", "latitude", "longitude", "context_summary", "user_name", "user_phone"]
                        }
                        """
                    }
                ]
            },
            new SwarmAgentDefinition
            {
                AgentId = "reviewer",
                Name = "Review Agent",
                Role = SwarmRole.Reviewer,
                Instructions = """
                    You are the final review agent. You receive the complete context from all prior agents.
                    Your job:
                    1. Verify all necessary steps were taken
                    2. Check for consistency across agent outputs
                    3. Ensure no PII is leaked in the response
                    4. Produce a concise, actionable summary for the system log
                    5. If anything is missing, hand back to the appropriate specialist

                    Produce your final summary as a structured report.
                    """,
                HandoffTargets = ["threat-assessor", "evidence-analyst", "location-resolver"],
                Tools =
                [
                    new SwarmToolDefinition
                    {
                        Name = "generate_report",
                        Description = "Generate the final structured incident report",
                        ParametersJson = """
                        {
                            "type": "object",
                            "properties": {
                                "incident_id": { "type": "string" },
                                "classification": { "type": "string" },
                                "severity": { "type": "integer" },
                                "actions_taken": { "type": "array", "items": { "type": "string" } },
                                "responders_dispatched": { "type": "integer" },
                                "first_responders_notified": { "type": "boolean" },
                                "summary": { "type": "string" }
                            },
                            "required": ["classification", "severity", "actions_taken", "summary"]
                        }
                        """
                    }
                ]
            }
        ]
    };

    /// <summary>
    /// Code review swarm — multi-perspective code review pipeline.
    /// Triage → security review → architecture review → style review → aggregator.
    /// </summary>
    public static SwarmDefinition CodeReviewSwarm() => new()
    {
        SwarmId = "code-review-swarm",
        Name = "Code Review Swarm",
        Description = "Multi-agent code review: security, architecture, style, and aggregated feedback.",
        EntryPointAgentId = "cr-triage",
        MaxHandoffDepth = 6,
        Agents =
        [
            new SwarmAgentDefinition
            {
                AgentId = "cr-triage",
                Name = "Code Review Triage",
                Role = SwarmRole.Triage,
                IsEntryPoint = true,
                Instructions = "You receive code diffs for review. Route to all three reviewers: security, architecture, and style. Start with security-reviewer.",
                HandoffTargets = ["security-reviewer", "arch-reviewer", "style-reviewer"]
            },
            new SwarmAgentDefinition
            {
                AgentId = "security-reviewer",
                Name = "Security Reviewer",
                Role = SwarmRole.Specialist,
                Instructions = "Review code for OWASP Top 10 vulnerabilities, injection risks, auth issues, secrets exposure, and unsafe deserialization. Report findings and hand off to arch-reviewer.",
                HandoffTargets = ["arch-reviewer", "cr-aggregator"],
                Tools =
                [
                    new SwarmToolDefinition
                    {
                        Name = "report_security_finding",
                        Description = "Report a security vulnerability found in the code",
                        ParametersJson = """{"type":"object","properties":{"severity":{"type":"string","enum":["critical","high","medium","low","info"]},"category":{"type":"string"},"description":{"type":"string"},"file":{"type":"string"},"line":{"type":"integer"},"recommendation":{"type":"string"}},"required":["severity","category","description"]}"""
                    }
                ]
            },
            new SwarmAgentDefinition
            {
                AgentId = "arch-reviewer",
                Name = "Architecture Reviewer",
                Role = SwarmRole.Specialist,
                Instructions = "Review code for architectural concerns: SOLID principles, hexagonal architecture compliance, port/adapter pattern adherence, dependency direction, and domain model integrity. Hand off to style-reviewer.",
                HandoffTargets = ["style-reviewer", "cr-aggregator"]
            },
            new SwarmAgentDefinition
            {
                AgentId = "style-reviewer",
                Name = "Style Reviewer",
                Role = SwarmRole.Specialist,
                Instructions = "Review code for style consistency: naming conventions, formatting, documentation quality, code duplication, and readability. Hand off to cr-aggregator.",
                HandoffTargets = ["cr-aggregator"]
            },
            new SwarmAgentDefinition
            {
                AgentId = "cr-aggregator",
                Name = "Review Aggregator",
                Role = SwarmRole.Aggregator,
                Instructions = "Collect all review findings from security, architecture, and style reviewers. Deduplicate, prioritize by severity, and produce a single consolidated review report with actionable items ranked by importance.",
                HandoffTargets = []
            }
        ]
    };

    /// <summary>
    /// Emergency dispatch swarm — streamlined for rapid response.
    /// Intake → threat assessment → resource allocation → notification → supervisor.
    /// </summary>
    public static SwarmDefinition EmergencyDispatchSwarm() => new()
    {
        SwarmId = "emergency-dispatch-swarm",
        Name = "Emergency Dispatch Swarm",
        Description = "Rapid emergency response: intake, threat assessment, resource allocation, and multi-channel notification.",
        EntryPointAgentId = "ed-intake",
        MaxConcurrentTasks = 20,
        MaxHandoffDepth = 5,
        AgentTurnTimeoutSeconds = 30,
        TaskTimeoutSeconds = 180,
        Agents =
        [
            new SwarmAgentDefinition
            {
                AgentId = "ed-intake",
                Name = "Emergency Intake",
                Role = SwarmRole.Triage,
                IsEntryPoint = true,
                Temperature = 0.3f,
                Instructions = "You rapidly classify emergency intake. Extract: location, threat type, number of people involved, injuries reported. Immediately hand off to ed-threat-assess.",
                HandoffTargets = ["ed-threat-assess"]
            },
            new SwarmAgentDefinition
            {
                AgentId = "ed-threat-assess",
                Name = "Threat Assessment",
                Role = SwarmRole.Specialist,
                Temperature = 0.2f,
                Instructions = "Assess threat level (1-5) and determine resource needs. For level 4-5: recommend 911. For all levels: determine volunteer count, radius, and dispatch strategy. Hand off to ed-resource-alloc.",
                HandoffTargets = ["ed-resource-alloc"]
            },
            new SwarmAgentDefinition
            {
                AgentId = "ed-resource-alloc",
                Name = "Resource Allocation",
                Role = SwarmRole.Specialist,
                Temperature = 0.1f,
                Instructions = "Allocate resources based on threat assessment. Query nearby responders, check availability, assign responders by proximity and certification. Hand off to ed-notify.",
                HandoffTargets = ["ed-notify"],
                Tools =
                [
                    new SwarmToolDefinition
                    {
                        Name = "allocate_responders",
                        Description = "Select and allocate responders from the available pool",
                        ParametersJson = """{"type":"object","properties":{"responder_ids":{"type":"array","items":{"type":"string"}},"dispatch_priority":{"type":"string","enum":["immediate","urgent","standard"]},"eta_minutes":{"type":"integer"}},"required":["responder_ids","dispatch_priority"]}"""
                    }
                ]
            },
            new SwarmAgentDefinition
            {
                AgentId = "ed-notify",
                Name = "Notification Agent",
                Role = SwarmRole.Specialist,
                Temperature = 0.1f,
                Instructions = """
                    Send notifications to all allocated responders via push notification, SMS, and in-app alert.
                    If the threat assessment flagged requires_911=true or threat_level >= 4,
                    you MUST also call initiate_911_call to contact emergency services on the user's behalf.
                    This places a Twilio voice call to the local PSAP with a TTS context summary and
                    pushes GPS coordinates to RapidSOS for the dispatcher's map display.
                    Produce a dispatch confirmation summary including whether 911 was notified.
                    """,
                HandoffTargets = ["ed-supervisor"],
                Tools =
                [
                    new SwarmToolDefinition
                    {
                        Name = "send_notification",
                        Description = "Send a notification through specified channels",
                        ParametersJson = """{"type":"object","properties":{"recipient_id":{"type":"string"},"channels":{"type":"array","items":{"type":"string","enum":["push","sms","in_app","voice_call"]}},"message":{"type":"string"},"priority":{"type":"string","enum":["normal","high","critical"]}},"required":["recipient_id","channels","message"]}"""
                    },
                    new SwarmToolDefinition
                    {
                        Name = "initiate_911_call",
                        Description = "Initiate a 911 emergency call on behalf of the user via Twilio/RapidSOS. Places a voice call to the local PSAP with TTS context and pushes GPS to the dispatcher's map.",
                        ParametersJson = """{"type":"object","properties":{"service_type":{"type":"string","enum":["Police","Fire","Ems","All"]},"latitude":{"type":"number"},"longitude":{"type":"number"},"context_summary":{"type":"string","description":"Brief summary for the 911 dispatcher"},"user_name":{"type":"string"},"user_phone":{"type":"string"},"medical_info":{"type":"string"},"volunteers_en_route":{"type":"integer"},"access_instructions":{"type":"string"}},"required":["service_type","latitude","longitude","context_summary","user_name","user_phone"]}"""
                    }
                ]
            },
            new SwarmAgentDefinition
            {
                AgentId = "ed-supervisor",
                Name = "Dispatch Supervisor",
                Role = SwarmRole.Supervisor,
                Instructions = "Review the complete dispatch chain. Verify all notifications were sent, resources allocated, and 911 contacted if needed. Produce final dispatch report. Flag any gaps for human review.",
                HandoffTargets = []
            }
        ]
    };

    /// <summary>
    /// Standards audit swarm — ISO/regulatory compliance gap analysis.
    /// Ingest → requirements mapper → gap analyzer → remediation planner.
    /// </summary>
    public static SwarmDefinition StandardsAuditSwarm() => new()
    {
        SwarmId = "standards-audit-swarm",
        Name = "Standards Audit Swarm",
        Description = "Audit system against ISO 27001/27002, NIST, or custom standards. Maps requirements, identifies gaps, and produces remediation plans.",
        EntryPointAgentId = "sa-ingest",
        MaxHandoffDepth = 6,
        TaskTimeoutSeconds = 900,
        Agents =
        [
            new SwarmAgentDefinition
            {
                AgentId = "sa-ingest",
                Name = "Standards Ingest",
                Role = SwarmRole.Triage,
                IsEntryPoint = true,
                Instructions = "Parse the input to determine which standards framework to audit against (ISO 27001, ISO 27002, NIST CSF, SOC 2, custom). Extract the system description and hand off to sa-mapper.",
                HandoffTargets = ["sa-mapper"]
            },
            new SwarmAgentDefinition
            {
                AgentId = "sa-mapper",
                Name = "Requirements Mapper",
                Role = SwarmRole.Specialist,
                Instructions = "Map the system's features and controls to specific standard requirements/controls. For each requirement, note whether there's a matching system capability. Hand off to sa-gap-analyzer.",
                HandoffTargets = ["sa-gap-analyzer"]
            },
            new SwarmAgentDefinition
            {
                AgentId = "sa-gap-analyzer",
                Name = "Gap Analyzer",
                Role = SwarmRole.Specialist,
                Instructions = "Analyze the mapping to identify gaps: requirements with no matching control, partially met requirements, and requirements that exceed the system's scope. Categorize gaps by risk level. Hand off to sa-remediation.",
                HandoffTargets = ["sa-remediation"]
            },
            new SwarmAgentDefinition
            {
                AgentId = "sa-remediation",
                Name = "Remediation Planner",
                Role = SwarmRole.Specialist,
                Instructions = "For each identified gap, produce a specific remediation plan: what needs to be implemented, estimated effort, priority, and dependencies. Produce a final audit report with all findings and recommendations.",
                HandoffTargets = []
            }
        ]
    };

    /// <summary>Returns all available preset names and descriptions.</summary>
    public static List<(string Id, string Name, string Description, int AgentCount)> ListPresets() =>
    [
        ("safety-report-pipeline", "Safety Report Pipeline", "SOS triage → evidence → location → dispatch → review", 7),
        ("code-review-swarm", "Code Review Swarm", "Security → architecture → style → aggregated review", 5),
        ("emergency-dispatch-swarm", "Emergency Dispatch Swarm", "Intake → threat → resources → notify → supervise", 5),
        ("standards-audit-swarm", "Standards Audit Swarm", "Ingest → map requirements → gap analysis → remediation", 4)
    ];

    /// <summary>Get a preset by ID.</summary>
    public static SwarmDefinition? GetPreset(string presetId) => presetId switch
    {
        "safety-report-pipeline" => SafetyReportPipeline(),
        "code-review-swarm" => CodeReviewSwarm(),
        "emergency-dispatch-swarm" => EmergencyDispatchSwarm(),
        "standards-audit-swarm" => StandardsAuditSwarm(),
        _ => null
    };
}
