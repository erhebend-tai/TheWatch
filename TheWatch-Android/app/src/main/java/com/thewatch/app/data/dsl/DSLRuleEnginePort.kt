/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         DSLRuleEnginePort.kt                                   │
 * │ Purpose:      Hexagonal port for TheWatch.DSL rule engine. Parses    │
 * │               deterministic rules for routing emergency events.      │
 * │               Rule model: condition -> action.                       │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: kotlinx.coroutines                                     │
 * │                                                                      │
 * │ DSL Syntax Example:                                                  │
 * │   RULE "high-severity-sos"                                           │
 * │     WHEN event.type == "SOS" AND event.severity == "HIGH"            │
 * │     THEN NOTIFY_RESPONDERS(radius: 2000m, count: 10)                 │
 * │     AND NOTIFY_911                                                   │
 * │   END                                                                │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val engine: DSLRuleEnginePort = hiltGet()                          │
 * │   val result = engine.evaluate(event)                                │
 * │   result.actions.forEach { it.execute() }                            │
 * │                                                                      │
 * │ NOTE: DETERMINISTIC. Same input = same output. No randomness.        │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.dsl

data class EmergencyEvent(
    val id: String, val type: String, val severity: String, val userId: String,
    val latitude: Double, val longitude: Double, val timestamp: Long,
    val triggerSource: String, val metadata: Map<String, String> = emptyMap()
)

data class RuleCondition(val field: String, val operator: String, val value: String, val conjunction: String = "AND")
data class RuleAction(val type: String, val parameters: Map<String, String> = emptyMap())

data class DSLRule(
    val name: String, val priority: Int = 0, val conditions: List<RuleCondition>,
    val actions: List<RuleAction>, val enabled: Boolean = true, val description: String = ""
)

data class EvaluationResult(
    val event: EmergencyEvent, val matchedRules: List<DSLRule>,
    val actions: List<RuleAction>, val evaluationTimeMs: Long, val ruleCount: Int
)

interface DSLRuleEnginePort {
    suspend fun loadRules(dslText: String): Result<Int>
    suspend fun loadCompiledRules(rules: List<DSLRule>)
    suspend fun evaluate(event: EmergencyEvent): EvaluationResult
    suspend fun getRules(): List<DSLRule>
    suspend fun setRuleEnabled(ruleName: String, enabled: Boolean)
    suspend fun validate(dslText: String): List<String>
    suspend fun getRuleCount(): Int
}
