/**
 * WRITE-AHEAD LOG | File: DSLRuleEngine.swift | Purpose: TheWatch.DSL rule engine for emergency routing
 * Created: 2026-03-24 | Author: Claude | Deps: Foundation
 * DSL: RULE "name" WHEN condition THEN action END
 * Usage: let result = await MockDSLRuleEngine().evaluate(EmergencyEventModel(type:"SOS",severity:"HIGH",...))
 * NOTE: DETERMINISTIC. Same input = same output.
 */
import Foundation

struct EmergencyEventModel { let id: String; let type: String; let severity: String; let userId: String; let latitude: Double; let longitude: Double; let timestamp: Date; let triggerSource: String; let metadata: [String: String]
    init(id: String = UUID().uuidString, type: String, severity: String, userId: String, latitude: Double = 0, longitude: Double = 0, timestamp: Date = Date(), triggerSource: String = "MANUAL", metadata: [String: String] = [:]) { self.id = id; self.type = type; self.severity = severity; self.userId = userId; self.latitude = latitude; self.longitude = longitude; self.timestamp = timestamp; self.triggerSource = triggerSource; self.metadata = metadata } }

struct DSLRuleCondition { let field: String; let op: String; let value: String; let conjunction: String; init(field: String, op: String, value: String, conjunction: String = "AND") { self.field = field; self.op = op; self.value = value; self.conjunction = conjunction } }
struct DSLRuleAction { let type: String; let parameters: [String: String]; init(type: String, parameters: [String: String] = [:]) { self.type = type; self.parameters = parameters } }
struct DSLRuleModel: Identifiable { let id = UUID(); let name: String; let priority: Int; let conditions: [DSLRuleCondition]; let actions: [DSLRuleAction]; var enabled: Bool; let description: String
    init(name: String, priority: Int = 0, conditions: [DSLRuleCondition], actions: [DSLRuleAction], enabled: Bool = true, description: String = "") { self.name = name; self.priority = priority; self.conditions = conditions; self.actions = actions; self.enabled = enabled; self.description = description } }
struct DSLEvaluationResult { let event: EmergencyEventModel; let matchedRules: [DSLRuleModel]; let actions: [DSLRuleAction]; let evalTimeMs: Int64; let ruleCount: Int }

protocol DSLRuleEngineProtocol: AnyObject, Sendable {
    func loadRules(dslText: String) async throws -> Int
    func loadCompiledRules(_ rules: [DSLRuleModel]) async
    func evaluate(_ event: EmergencyEventModel) async -> DSLEvaluationResult
    func getRules() async -> [DSLRuleModel]
    func setRuleEnabled(name: String, enabled: Bool) async
    func validate(dslText: String) async -> [String]
    func getRuleCount() async -> Int
}

@Observable final class MockDSLRuleEngine: DSLRuleEngineProtocol, @unchecked Sendable {
    private var rules: [DSLRuleModel] = Self.defaults()

    func loadRules(dslText: String) async throws -> Int {
        let pat = try NSRegularExpression(pattern: "RULE\\s+\".*?\""); let c = pat.numberOfMatches(in: dslText, range: NSRange(dslText.startIndex..., in: dslText))
        guard c > 0 else { throw NSError(domain: "DSL", code: 1, userInfo: [NSLocalizedDescriptionKey: "No rules found"]) }; return c
    }
    func loadCompiledRules(_ rules: [DSLRuleModel]) async { self.rules = rules }
    func evaluate(_ event: EmergencyEventModel) async -> DSLEvaluationResult {
        let start = DispatchTime.now()
        let matched = rules.filter { $0.enabled }.sorted { $0.priority > $1.priority }.filter { matches($0.conditions, event) }
        let ns = DispatchTime.now().uptimeNanoseconds - start.uptimeNanoseconds
        return DSLEvaluationResult(event: event, matchedRules: matched, actions: matched.flatMap { $0.actions }, evalTimeMs: Int64(ns / 1_000_000), ruleCount: rules.count)
    }
    func getRules() async -> [DSLRuleModel] { rules }
    func setRuleEnabled(name: String, enabled: Bool) async { if let i = rules.firstIndex(where: { $0.name == name }) { rules[i].enabled = enabled } }
    func validate(dslText: String) async -> [String] { var e: [String] = []; if !dslText.contains("RULE") { e.append("No RULE") }; if !dslText.contains("WHEN") { e.append("No WHEN") }; if !dslText.contains("THEN") { e.append("No THEN") }; if !dslText.contains("END") { e.append("No END") }; return e }
    func getRuleCount() async -> Int { rules.count }

    private func matches(_ conds: [DSLRuleCondition], _ ev: EmergencyEventModel) -> Bool {
        conds.allSatisfy { c in let v = field(ev, c.field); switch c.op { case "==": return v == c.value; case "!=": return v != c.value; case "IN": return c.value.components(separatedBy: ",").map { $0.trimmingCharacters(in: .whitespaces) }.contains(v ?? ""); default: return false } }
    }
    private func field(_ ev: EmergencyEventModel, _ f: String) -> String? { switch f { case "event.type": return ev.type; case "event.severity": return ev.severity; case "event.triggerSource": return ev.triggerSource; default: return ev.metadata[f.replacingOccurrences(of: "event.metadata.", with: "")] } }

    static func defaults() -> [DSLRuleModel] { [
        DSLRuleModel(name: "high-severity-sos", priority: 100, conditions: [DSLRuleCondition(field: "event.type", op: "==", value: "SOS"), DSLRuleCondition(field: "event.severity", op: "IN", value: "HIGH,CRITICAL")], actions: [DSLRuleAction(type: "NOTIFY_RESPONDERS", parameters: ["radius": "2000", "count": "10"]), DSLRuleAction(type: "NOTIFY_911", parameters: ["include_location": "true"]), DSLRuleAction(type: "ALERT_CONTACTS", parameters: ["all": "true"])], description: "Critical SOS"),
        DSLRuleModel(name: "medium-sos", priority: 80, conditions: [DSLRuleCondition(field: "event.type", op: "==", value: "SOS"), DSLRuleCondition(field: "event.severity", op: "==", value: "MEDIUM")], actions: [DSLRuleAction(type: "NOTIFY_RESPONDERS", parameters: ["radius": "1500", "count": "5"])], description: "Standard SOS"),
        DSLRuleModel(name: "check-in", priority: 50, conditions: [DSLRuleCondition(field: "event.type", op: "==", value: "CHECK_IN")], actions: [DSLRuleAction(type: "NOTIFY_RESPONDERS", parameters: ["radius": "1000", "count": "3"])], description: "Wellness check-in"),
        DSLRuleModel(name: "auto-escalation", priority: 90, conditions: [DSLRuleCondition(field: "event.triggerSource", op: "==", value: "AUTO_ESCALATION")], actions: [DSLRuleAction(type: "ESCALATE", parameters: ["new_severity": "HIGH"]), DSLRuleAction(type: "NOTIFY_911")], description: "Auto-escalate"),
        DSLRuleModel(name: "phrase-detection", priority: 95, conditions: [DSLRuleCondition(field: "event.triggerSource", op: "==", value: "PHRASE_DETECTION")], actions: [DSLRuleAction(type: "NOTIFY_RESPONDERS", parameters: ["radius": "2000", "count": "10"])], description: "Emergency phrase")
    ] }
}
