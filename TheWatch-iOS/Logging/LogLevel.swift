import Foundation

/// Structured log levels mirroring Serilog on the Aspire backend.
/// Raw values are ordered by severity for comparison.
enum LogLevel: Int, Codable, Comparable, CaseIterable {
    case verbose = 0
    case debug = 1
    case information = 2
    case warning = 3
    case error = 4
    case fatal = 5

    var label: String {
        switch self {
        case .verbose:     return "VRB"
        case .debug:       return "DBG"
        case .information: return "INF"
        case .warning:     return "WRN"
        case .error:       return "ERR"
        case .fatal:       return "FTL"
        }
    }

    var emoji: String {
        switch self {
        case .verbose:     return "⚪"
        case .debug:       return "🔵"
        case .information: return "🟢"
        case .warning:     return "🟡"
        case .error:       return "🔴"
        case .fatal:       return "💀"
        }
    }

    static func < (lhs: LogLevel, rhs: LogLevel) -> Bool {
        lhs.rawValue < rhs.rawValue
    }
}
