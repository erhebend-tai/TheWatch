// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         DeviceIntegrityService.swift
// Purpose:      Protocol (port) and implementations for device integrity
//               checks on iOS. Detects jailbreak indicators including Cydia,
//               suspicious file paths, writable system directories, fork()
//               behavior, symbolic link anomalies, and dynamic library
//               injection. Uses hexagonal port/adapter pattern.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, UIKit, Darwin (for fork/dlopen)
// Related:      CertificatePinningService.swift (network security),
//               TheWatchApp.swift (integrity check on launch)
//
// Usage Example:
//   let integrityService: DeviceIntegrityPort = DeviceIntegrityService()
//   let report = integrityService.performFullCheck()
//   if !report.isDeviceIntact {
//       print("Integrity violations: \(report.violations)")
//       // Show warning, disable sensitive features, or log for audit
//   }
//
// Mock Usage:
//   let mockService: DeviceIntegrityPort = MockDeviceIntegrityService()
//   // mockService.isJailbroken = true  // for testing jailbreak UI
//
// Security Model:
//   - Life-safety apps must operate on compromised devices (users in danger
//     may only have a jailbroken device available). Therefore:
//     * We WARN but do NOT block functionality on jailbroken devices
//     * We log the integrity status for audit/compliance
//     * We may disable certain sensitive features (e.g., biometric auth
//       bypass) on compromised devices
//     * SOS functionality must ALWAYS work regardless of device integrity
//
// Detection Methods (defense in depth):
//   1. File-based: Check for Cydia, Sileo, known jailbreak files
//   2. Directory-based: Check for writable system paths
//   3. URL scheme: Check if Cydia URL scheme is registered
//   4. Fork behavior: fork() should fail on non-jailbroken iOS
//   5. Sandbox: Attempt to write outside sandbox
//   6. Dylib injection: Check for suspicious loaded libraries
//   7. Symbolic links: /Applications should not be a symlink
//   8. Environment: Check for DYLD_INSERT_LIBRARIES
//
// Potential Additions:
//   - Apple App Attest (DeviceCheck framework) for server-side validation
//   - Code signing integrity verification (embedded.mobileprovision)
//   - Runtime integrity (method swizzle detection via objc_getClass)
//   - Frida/Cycript detection (port scanning, named pipes)
//   - Debugger detection (sysctl, ptrace)
//   - Emulator detection (model name, CPU architecture checks)
//   - Screenshot/screen recording detection for sensitive screens
// ============================================================================

import Foundation
import UIKit

// MARK: - Integrity Check Result

/// Detailed report of device integrity checks.
struct DeviceIntegrityReport: Sendable {
    /// Whether the device passed all integrity checks
    let isDeviceIntact: Bool

    /// List of specific integrity violations found
    let violations: [IntegrityViolation]

    /// Timestamp of the check
    let checkedAt: Date

    /// Device model identifier
    let deviceModel: String

    /// iOS version
    let osVersion: String

    /// Overall risk level
    var riskLevel: RiskLevel {
        if violations.isEmpty { return .none }
        if violations.contains(where: { $0.severity == .critical }) { return .critical }
        if violations.contains(where: { $0.severity == .high }) { return .high }
        return .medium
    }
}

/// A specific integrity violation found during checks.
struct IntegrityViolation: Sendable {
    let checkName: String
    let description: String
    let severity: ViolationSeverity
}

enum ViolationSeverity: String, Sendable, Comparable {
    case low = "Low"
    case medium = "Medium"
    case high = "High"
    case critical = "Critical"

    static func < (lhs: ViolationSeverity, rhs: ViolationSeverity) -> Bool {
        let order: [ViolationSeverity] = [.low, .medium, .high, .critical]
        return (order.firstIndex(of: lhs) ?? 0) < (order.firstIndex(of: rhs) ?? 0)
    }
}

enum RiskLevel: String, Sendable {
    case none = "None"
    case medium = "Medium"
    case high = "High"
    case critical = "Critical"
}

// MARK: - Device Integrity Port (Protocol)

/// Port protocol for device integrity checking. Adapters implement either
/// real integrity checks or mock behavior for testing.
protocol DeviceIntegrityPort: Sendable {
    /// Perform all integrity checks and return a detailed report.
    func performFullCheck() -> DeviceIntegrityReport

    /// Quick check: is the device likely jailbroken?
    func isDeviceCompromised() -> Bool
}

// MARK: - Production Device Integrity Service

/// Production implementation that performs actual device integrity checks.
/// Uses multiple detection vectors for defense-in-depth.
final class DeviceIntegrityService: DeviceIntegrityPort, @unchecked Sendable {

    // MARK: - Suspicious File Paths

    /// Known jailbreak-related file paths to check for existence
    private static let suspiciousFilePaths: [String] = [
        // Cydia and package managers
        "/Applications/Cydia.app",
        "/Applications/Sileo.app",
        "/Applications/Zebra.app",
        "/Applications/Installer.app",

        // Jailbreak tools and artifacts
        "/Library/MobileSubstrate/MobileSubstrate.dylib",
        "/Library/MobileSubstrate/DynamicLibraries",
        "/usr/sbin/sshd",
        "/usr/bin/sshd",
        "/usr/libexec/sftp-server",
        "/etc/apt",
        "/etc/apt/sources.list.d",
        "/private/var/lib/apt/",
        "/private/var/lib/cydia",
        "/private/var/stash",
        "/private/var/mobile/Library/SBSettings/Themes",

        // Common jailbreak binaries
        "/bin/bash",
        "/bin/sh",  // Should exist but writable = problem
        "/usr/bin/ssh",
        "/usr/bin/cycript",
        "/usr/local/bin/cycript",
        "/usr/lib/libcycript.dylib",

        // Substrate and Substitute
        "/Library/MobileSubstrate",
        "/usr/lib/TweakInject",
        "/usr/lib/substitute-inserter.dylib",
        "/usr/lib/substrate",
        "/usr/lib/libsubstitute.dylib",

        // Package manager databases
        "/var/cache/apt",
        "/var/lib/apt",
        "/var/lib/dpkg",

        // checkra1n and other tools
        "/private/var/checkra1n.dmg",
        "/private/var/binpack",

        // Frida (runtime instrumentation)
        "/usr/lib/frida",
        "/usr/bin/frida-server",
        "/usr/local/bin/frida-server"
    ]

    /// Suspicious URL schemes that indicate jailbreak tools
    private static let suspiciousURLSchemes: [String] = [
        "cydia://",
        "sileo://",
        "zbra://",          // Zebra
        "filza://",         // Filza file manager
        "undecimus://",     // unc0ver
        "activator://"      // Activator tweak
    ]

    // MARK: - Full Check

    func performFullCheck() -> DeviceIntegrityReport {
        var violations: [IntegrityViolation] = []

        // 1. File-based checks
        violations.append(contentsOf: checkSuspiciousFiles())

        // 2. URL scheme checks
        violations.append(contentsOf: checkSuspiciousURLSchemes())

        // 3. Writable system directory check
        violations.append(contentsOf: checkWritableSystemPaths())

        // 4. Sandbox escape check
        violations.append(contentsOf: checkSandboxIntegrity())

        // 5. Fork behavior check
        violations.append(contentsOf: checkForkBehavior())

        // 6. Symbolic link checks
        violations.append(contentsOf: checkSymbolicLinks())

        // 7. Dynamic library injection check
        violations.append(contentsOf: checkDylibInjection())

        // 8. Environment variable check
        violations.append(contentsOf: checkEnvironmentVariables())

        let report = DeviceIntegrityReport(
            isDeviceIntact: violations.isEmpty,
            violations: violations,
            checkedAt: Date(),
            deviceModel: UIDevice.current.model,
            osVersion: UIDevice.current.systemVersion
        )

        if !report.isDeviceIntact {
            print("[DeviceIntegrity] WARNING: Device integrity check FAILED")
            print("[DeviceIntegrity] Risk level: \(report.riskLevel.rawValue)")
            for violation in violations {
                print("[DeviceIntegrity]   [\(violation.severity.rawValue)] \(violation.checkName): \(violation.description)")
            }
        } else {
            print("[DeviceIntegrity] All integrity checks passed")
        }

        return report
    }

    func isDeviceCompromised() -> Bool {
        return !performFullCheck().isDeviceIntact
    }

    // MARK: - Individual Checks

    /// Check for existence of known jailbreak files
    private func checkSuspiciousFiles() -> [IntegrityViolation] {
        var violations: [IntegrityViolation] = []
        let fileManager = FileManager.default

        for path in Self.suspiciousFilePaths {
            if fileManager.fileExists(atPath: path) {
                violations.append(IntegrityViolation(
                    checkName: "SuspiciousFile",
                    description: "Found suspicious file: \(path)",
                    severity: path.contains("Cydia") || path.contains("Substrate")
                        ? .critical : .high
                ))
            }
        }

        return violations
    }

    /// Check if jailbreak URL schemes are registered
    private func checkSuspiciousURLSchemes() -> [IntegrityViolation] {
        var violations: [IntegrityViolation] = []

        // NOTE: This must run on the main thread (UIApplication)
        DispatchQueue.main.sync {
            for scheme in Self.suspiciousURLSchemes {
                if let url = URL(string: scheme),
                   UIApplication.shared.canOpenURL(url) {
                    violations.append(IntegrityViolation(
                        checkName: "SuspiciousURLScheme",
                        description: "Device can open jailbreak URL scheme: \(scheme)",
                        severity: .high
                    ))
                }
            }
        }

        return violations
    }

    /// Check if system directories are writable (they shouldn't be)
    private func checkWritableSystemPaths() -> [IntegrityViolation] {
        var violations: [IntegrityViolation] = []
        let systemPaths = ["/private/", "/root/"]

        for path in systemPaths {
            let testFile = path + ".thewatch_integrity_test_\(UUID().uuidString)"
            do {
                try "test".write(toFile: testFile, atomically: true, encoding: .utf8)
                // If we got here, the write succeeded - BAD
                try? FileManager.default.removeItem(atPath: testFile)
                violations.append(IntegrityViolation(
                    checkName: "WritableSystemPath",
                    description: "System path is writable: \(path)",
                    severity: .critical
                ))
            } catch {
                // Expected: write should fail on non-jailbroken device
            }
        }

        return violations
    }

    /// Check sandbox integrity by attempting to access outside sandbox
    private func checkSandboxIntegrity() -> [IntegrityViolation] {
        var violations: [IntegrityViolation] = []

        let outsideSandboxPath = "/private/var/mobile/.thewatch_sandbox_test"
        do {
            try "test".write(
                toFile: outsideSandboxPath,
                atomically: true,
                encoding: .utf8
            )
            try? FileManager.default.removeItem(atPath: outsideSandboxPath)
            violations.append(IntegrityViolation(
                checkName: "SandboxEscape",
                description: "Able to write outside application sandbox",
                severity: .critical
            ))
        } catch {
            // Expected
        }

        return violations
    }

    /// Check fork() behavior. On non-jailbroken iOS, fork() should fail.
    private func checkForkBehavior() -> [IntegrityViolation] {
        var violations: [IntegrityViolation] = []

        // fork() is not available in the iOS sandbox normally.
        // We use the Darwin/POSIX fork() which should return -1 on stock iOS.
        let result = fork()
        if result >= 0 {
            // fork() succeeded - this should not happen on stock iOS
            if result == 0 {
                // Child process - exit immediately
                _exit(0)
            }
            violations.append(IntegrityViolation(
                checkName: "ForkBehavior",
                description: "fork() succeeded, indicating sandbox is broken",
                severity: .critical
            ))
        }
        // result == -1 is expected (fork failed = good, sandbox is intact)

        return violations
    }

    /// Check for suspicious symbolic links
    private func checkSymbolicLinks() -> [IntegrityViolation] {
        var violations: [IntegrityViolation] = []
        let pathsToCheck = ["/Applications", "/Library/Ringtones", "/Library/Wallpaper"]

        let fileManager = FileManager.default
        for path in pathsToCheck {
            do {
                let attributes = try fileManager.attributesOfItem(atPath: path)
                if let type = attributes[.type] as? FileAttributeType,
                   type == .typeSymbolicLink {
                    violations.append(IntegrityViolation(
                        checkName: "SymbolicLink",
                        description: "\(path) is a symbolic link (expected directory)",
                        severity: .high
                    ))
                }
            } catch {
                // Path doesn't exist or not accessible - OK
            }
        }

        return violations
    }

    /// Check for injected dynamic libraries
    private func checkDylibInjection() -> [IntegrityViolation] {
        var violations: [IntegrityViolation] = []

        // Check loaded image count for suspicious libraries
        let imageCount = _dyld_image_count()
        let suspiciousLibs = [
            "MobileSubstrate",
            "SubstrateLoader",
            "cycript",
            "SSLKillSwitch",
            "FridaGadget",
            "frida-agent",
            "libcycript",
            "TweakInject",
            "substitute"
        ]

        for i in 0..<imageCount {
            if let imageName = _dyld_get_image_name(i) {
                let name = String(cString: imageName)
                for suspicious in suspiciousLibs {
                    if name.lowercased().contains(suspicious.lowercased()) {
                        violations.append(IntegrityViolation(
                            checkName: "DylibInjection",
                            description: "Suspicious library loaded: \(name)",
                            severity: .critical
                        ))
                    }
                }
            }
        }

        return violations
    }

    /// Check for suspicious environment variables
    private func checkEnvironmentVariables() -> [IntegrityViolation] {
        var violations: [IntegrityViolation] = []

        // DYLD_INSERT_LIBRARIES is used for code injection
        if let insertLibs = ProcessInfo.processInfo.environment["DYLD_INSERT_LIBRARIES"] {
            violations.append(IntegrityViolation(
                checkName: "EnvironmentInjection",
                description: "DYLD_INSERT_LIBRARIES is set: \(insertLibs)",
                severity: .critical
            ))
        }

        // _MSSafeMode indicates Substrate is loaded
        if ProcessInfo.processInfo.environment["_MSSafeMode"] != nil {
            violations.append(IntegrityViolation(
                checkName: "SubstrateSafeMode",
                description: "_MSSafeMode environment variable present",
                severity: .high
            ))
        }

        return violations
    }
}

// MARK: - Mock Device Integrity Service

/// Mock implementation for development and testing.
/// Allows configuring whether the device appears compromised.
final class MockDeviceIntegrityService: DeviceIntegrityPort, @unchecked Sendable {

    /// Set to `true` to simulate a jailbroken device
    var simulateJailbreak: Bool = false

    /// Custom violations to return when simulating jailbreak
    var customViolations: [IntegrityViolation] = []

    func performFullCheck() -> DeviceIntegrityReport {
        if simulateJailbreak {
            let violations = customViolations.isEmpty
                ? [
                    IntegrityViolation(
                        checkName: "MockJailbreak",
                        description: "Simulated jailbreak for testing",
                        severity: .critical
                    )
                ]
                : customViolations

            return DeviceIntegrityReport(
                isDeviceIntact: false,
                violations: violations,
                checkedAt: Date(),
                deviceModel: "iPhone (Mock)",
                osVersion: "17.0 (Mock)"
            )
        }

        return DeviceIntegrityReport(
            isDeviceIntact: true,
            violations: [],
            checkedAt: Date(),
            deviceModel: "iPhone (Mock)",
            osVersion: "17.0 (Mock)"
        )
    }

    func isDeviceCompromised() -> Bool {
        return simulateJailbreak
    }
}
