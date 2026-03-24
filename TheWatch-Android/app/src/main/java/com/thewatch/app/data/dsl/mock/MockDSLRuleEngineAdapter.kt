/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockDSLRuleEngineAdapter.kt                            │
 * │ Purpose:      Mock adapter for DSLRuleEnginePort. Preloaded rules.   │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: DSLRuleEnginePort                                      │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val mock = MockDSLRuleEngineAdapter()                              │
 * │   val result = mock.evaluate(EmergencyEvent(type="SOS",...))         │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.dsl.mock

import com.thewatch.app.data.dsl.*
import kotlinx.coroutines.delay

class MockDSLRuleEngineAdapter : DSLRuleEnginePort {
    private val rules = mutableListOf<DSLRule>().apply { addAll(defaultRules()) }

    override suspend fun loadRules(dslText: String): Result<Int> {
        delay(300)
        val count = Regex("RULE\\s+\".*?\"").findAll(dslText).count()
        return if (count > 0) Result.success(count) else Result.failure(IllegalArgumentException("No valid rules"))
    }

    override suspend fun loadCompiledRules(rules: List<DSLRule>) { this.rules.clear(); this.rules.addAll(rules) }

    override suspend fun evaluate(event: EmergencyEvent): EvaluationResult {
        val start = System.currentTimeMillis()
        val matched = rules.filter { it.enabled }.sortedByDescending { it.priority }.filter { matchesConditions(it.conditions, event) }
        return EvaluationResult(event, matched, matched.flatMap { it.actions }, System.currentTimeMillis() - start, rules.size)
    }

    override suspend fun getRules() = rules.toList()
    override suspend fun setRuleEnabled(ruleName: String, enabled: Boolean) { rules.indexOfFirst { it.name == ruleName }.takeIf { it >= 0 }?.let { rules[it] = rules[it].copy(enabled = enabled) } }
    override suspend fun validate(dslText: String): List<String> { delay(200); return mutableListOf<String>().apply { if ("RULE" !in dslText) add("No RULE keyword"); if ("WHEN" !in dslText) add("No WHEN clause"); if ("THEN" !in dslText) add("No THEN clause"); if ("END" !in dslText) add("No END keyword") } }
    override suspend fun getRuleCount() = rules.size

    private fun matchesConditions(conditions: List<RuleCondition>, event: EmergencyEvent) = conditions.all { c ->
        val v = when (c.field) { "event.type" -> event.type; "event.severity" -> event.severity; "event.triggerSource" -> event.triggerSource; else -> event.metadata[c.field.removePrefix("event.metadata.")] }
        when (c.operator) { "==" -> v == c.value; "!=" -> v != c.value; "IN" -> c.value.split(",").map { it.trim() }.contains(v); else -> false }
    }

    private fun defaultRules() = listOf(
        DSLRule("high-severity-sos", 100, listOf(RuleCondition("event.type", "==", "SOS"), RuleCondition("event.severity", "IN", "HIGH,CRITICAL")),
            listOf(RuleAction("NOTIFY_RESPONDERS", mapOf("radius" to "2000", "count" to "10")), RuleAction("NOTIFY_911", mapOf("include_location" to "true")), RuleAction("ALERT_CONTACTS", mapOf("all" to "true"))), description = "Critical SOS"),
        DSLRule("medium-severity-sos", 80, listOf(RuleCondition("event.type", "==", "SOS"), RuleCondition("event.severity", "==", "MEDIUM")),
            listOf(RuleAction("NOTIFY_RESPONDERS", mapOf("radius" to "1500", "count" to "5")), RuleAction("ALERT_CONTACTS", mapOf("priority" to "1"))), description = "Standard SOS"),
        DSLRule("check-in-request", 50, listOf(RuleCondition("event.type", "==", "CHECK_IN")),
            listOf(RuleAction("NOTIFY_RESPONDERS", mapOf("radius" to "1000", "count" to "3"))), description = "Wellness check-in"),
        DSLRule("auto-escalation", 90, listOf(RuleCondition("event.triggerSource", "==", "AUTO_ESCALATION")),
            listOf(RuleAction("ESCALATE", mapOf("new_severity" to "HIGH")), RuleAction("NOTIFY_911", mapOf("include_location" to "true"))), description = "Auto-escalate unanswered"),
        DSLRule("phrase-detection", 95, listOf(RuleCondition("event.triggerSource", "==", "PHRASE_DETECTION")),
            listOf(RuleAction("NOTIFY_RESPONDERS", mapOf("radius" to "2000", "count" to "10"))), description = "Emergency phrase detected")
    )
}
