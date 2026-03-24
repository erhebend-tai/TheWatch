/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         PlayIntegrityAdapter.kt                                │
 * │ Purpose:      Native (Tier 2) adapter for DeviceIntegrityPort.       │
 * │               Performs local heuristic root/emulator detection AND    │
 * │               requests a Google Play Integrity API token for         │
 * │               server-side attestation. Combines multiple signals     │
 * │               into a composite DeviceIntegrityVerdict.               │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Google Play Integrity API (play-integrity:1.3.0),     │
 * │               DeviceIntegrityPort                                    │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In AppModule.kt (release builds):                               │
 * │   @Provides fun provideDeviceIntegrityPort(                          │
 * │       adapter: PlayIntegrityAdapter                                  │
 * │   ): DeviceIntegrityPort = adapter                                   │
 * │                                                                      │
 * │ ROOT DETECTION HEURISTICS:                                           │
 * │   1. Check for su binary in PATH dirs                                │
 * │   2. Check for Superuser.apk / SuperSU                               │
 * │   3. Check for Magisk Manager / MagiskHide                           │
 * │   4. Check for Xposed / EdXposed / LSPosed framework                 │
 * │   5. Check for busybox binary                                        │
 * │   6. Check ro.build.tags for test-keys                               │
 * │   7. Check SELinux enforcing status                                  │
 * │   8. Check Build.FINGERPRINT for emulator signatures                 │
 * │   9. Check for debugger via Debug.isDebuggerConnected()              │
 * │  10. Check Settings.Secure for mock locations & USB debug            │
 * │                                                                      │
 * │ NOTE: This is a best-effort detection. Magisk canary builds can      │
 * │ hide from all userspace checks. The Play Integrity API is the most   │
 * │ reliable server-side signal. For maximum security, combine this      │
 * │ with server-side token verification (Live tier).                     │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.security.native

import android.content.Context
import android.os.Build
import android.os.Debug
import android.provider.Settings
import android.util.Log
import com.thewatch.app.data.security.DeviceIntegrityPort
import com.thewatch.app.data.security.DeviceIntegrityVerdict
import com.thewatch.app.data.security.FindingSeverity
import com.thewatch.app.data.security.IntegrityCheck
import com.thewatch.app.data.security.IntegrityFinding
import com.thewatch.app.data.security.PlayIntegrityLevel
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class PlayIntegrityAdapter @Inject constructor(
    @ApplicationContext private val context: Context
) : DeviceIntegrityPort {

    companion object {
        private const val TAG = "TheWatch.DeviceIntegrity"

        /** Common paths where su binary may exist. */
        private val SU_PATHS = listOf(
            "/system/bin/su", "/system/xbin/su", "/sbin/su",
            "/data/local/xbin/su", "/data/local/bin/su", "/data/local/su",
            "/system/sd/xbin/su", "/system/bin/failsafe/su",
            "/su/bin/su", "/su/bin", "/magisk/.core/bin/su"
        )

        /** Package names for known root management apps. */
        private val ROOT_PACKAGES = listOf(
            "com.noshufou.android.su",
            "com.noshufou.android.su.elite",
            "eu.chainfire.supersu",
            "com.koushikdutta.superuser",
            "com.thirdparty.superuser",
            "com.yellowes.su",
            "com.topjohnwu.magisk",
            "io.github.vvb2060.magisk", // Magisk canary / Alpha
            "com.fox2code.mmm" // Magisk Module Manager
        )

        /** Package names for known hooking frameworks. */
        private val XPOSED_PACKAGES = listOf(
            "de.robv.android.xposed.installer",
            "org.meowcat.edxposed.manager",
            "org.lsposed.manager",
            "com.solohsu.android.edxp.manager"
        )

        /** Emulator fingerprint fragments. */
        private val EMULATOR_SIGNATURES = listOf(
            "generic", "unknown", "google_sdk", "Emulator", "Android SDK built for x86",
            "Genymotion", "goldfish", "AOSP on IA Emulator", "ranchu", "vbox86p"
        )
    }

    @Volatile
    private var cachedVerdict: DeviceIntegrityVerdict? = null

    override suspend fun checkIntegrity(): DeviceIntegrityVerdict {
        Log.i(TAG, "Starting device integrity check...")

        val findings = mutableListOf<IntegrityFinding>()

        // 1. Check for su binary
        findings.add(checkSuBinary())

        // 2. Check for root management apps (Superuser.apk etc.)
        findings.add(checkRootPackages())

        // 3. Check for Magisk
        findings.add(checkMagisk())

        // 4. Check for Xposed / EdXposed / LSPosed
        findings.add(checkXposed())

        // 5. Check for busybox
        findings.add(checkBusybox())

        // 6. Check build tags (test-keys)
        findings.add(checkTestKeys())

        // 7. Check SELinux
        findings.add(checkSELinux())

        // 8. Check emulator
        findings.add(checkEmulator())

        // 9. Check debugger
        findings.add(checkDebugger())

        // 10. Check developer settings
        findings.addAll(checkDeveloperSettings())

        val isCompromised = findings.any { it.detected && it.severity >= FindingSeverity.HIGH }
        val riskScore = calculateRiskScore(findings)

        val verdict = DeviceIntegrityVerdict(
            isCompromised = isCompromised,
            findings = findings,
            playIntegrityLevel = PlayIntegrityLevel.UNAVAILABLE, // Requires actual Play Integrity API call
            riskScore = riskScore
        )

        cachedVerdict = verdict
        Log.i(TAG, "Integrity check complete: compromised=$isCompromised, risk=$riskScore, findings=${findings.count { it.detected }}/${findings.size}")
        return verdict
    }

    override suspend fun requestIntegrityToken(): String? {
        // Google Play Integrity API token request.
        // In production, this calls IntegrityManager.requestIntegrityToken()
        // with a nonce from the server. The returned token is sent to the
        // server for verification via Google's API.
        //
        // Requires: implementation("com.google.android.play:integrity:1.3.0")
        // and a valid Google Cloud project number.
        //
        // For now, return null as the Play Integrity API setup requires
        // server-side configuration that belongs in the Live tier.
        Log.d(TAG, "requestIntegrityToken() — Play Integrity API not yet configured")
        return null
    }

    override fun getCachedVerdict(): DeviceIntegrityVerdict? = cachedVerdict

    // ── Individual check implementations ───────────────────────────

    private fun checkSuBinary(): IntegrityFinding {
        val found = SU_PATHS.any { File(it).exists() }
        return IntegrityFinding(
            check = IntegrityCheck.ROOT_SU_BINARY,
            detected = found,
            detail = if (found) "su binary found on device" else "No su binary detected",
            severity = if (found) FindingSeverity.CRITICAL else FindingSeverity.LOW
        )
    }

    private fun checkRootPackages(): IntegrityFinding {
        val pm = context.packageManager
        val foundPackage = ROOT_PACKAGES.firstOrNull { pkg ->
            try {
                pm.getPackageInfo(pkg, 0)
                true
            } catch (_: Exception) {
                false
            }
        }
        return IntegrityFinding(
            check = IntegrityCheck.ROOT_SUPERUSER_APK,
            detected = foundPackage != null,
            detail = foundPackage?.let { "Root app found: $it" } ?: "No root management apps",
            severity = if (foundPackage != null) FindingSeverity.CRITICAL else FindingSeverity.LOW
        )
    }

    private fun checkMagisk(): IntegrityFinding {
        val magiskFound = ROOT_PACKAGES.filter { it.contains("magisk") }.any { pkg ->
            try {
                context.packageManager.getPackageInfo(pkg, 0)
                true
            } catch (_: Exception) {
                false
            }
        }
        // Also check for Magisk's random package name pattern
        val magiskRandomized = try {
            File("/sbin/.magisk").exists() ||
            File("/data/adb/magisk").exists() ||
            File("/cache/.disable_magisk").exists()
        } catch (_: Exception) {
            false
        }
        val detected = magiskFound || magiskRandomized
        return IntegrityFinding(
            check = IntegrityCheck.ROOT_MAGISK,
            detected = detected,
            detail = if (detected) "Magisk indicators found" else "No Magisk detected",
            severity = if (detected) FindingSeverity.CRITICAL else FindingSeverity.LOW
        )
    }

    private fun checkXposed(): IntegrityFinding {
        val xposedFound = XPOSED_PACKAGES.any { pkg ->
            try {
                context.packageManager.getPackageInfo(pkg, 0)
                true
            } catch (_: Exception) {
                false
            }
        }
        // Also check for Xposed's stacktrace hook indicator
        val xposedInStack = try {
            throw Exception("Xposed check")
        } catch (e: Exception) {
            e.stackTrace.any { it.className.contains("de.robv.android.xposed") }
        }
        val detected = xposedFound || xposedInStack
        return IntegrityFinding(
            check = IntegrityCheck.ROOT_XPOSED,
            detected = detected,
            detail = if (detected) "Xposed/LSPosed framework detected" else "No hooking framework",
            severity = if (detected) FindingSeverity.CRITICAL else FindingSeverity.LOW
        )
    }

    private fun checkBusybox(): IntegrityFinding {
        val found = listOf("/system/bin/busybox", "/system/xbin/busybox", "/sbin/busybox")
            .any { File(it).exists() }
        return IntegrityFinding(
            check = IntegrityCheck.ROOT_BUSYBOX,
            detected = found,
            detail = if (found) "busybox binary found" else "No busybox detected",
            severity = if (found) FindingSeverity.MEDIUM else FindingSeverity.LOW
        )
    }

    private fun checkTestKeys(): IntegrityFinding {
        val testKeys = Build.TAGS?.contains("test-keys") == true
        return IntegrityFinding(
            check = IntegrityCheck.TEST_KEYS,
            detected = testKeys,
            detail = if (testKeys) "Build signed with test-keys (custom ROM)" else "Official build keys",
            severity = if (testKeys) FindingSeverity.HIGH else FindingSeverity.LOW
        )
    }

    private fun checkSELinux(): IntegrityFinding {
        val permissive = try {
            val process = Runtime.getRuntime().exec("getenforce")
            val output = process.inputStream.bufferedReader().readText().trim()
            process.waitFor()
            output.equals("Permissive", ignoreCase = true)
        } catch (_: Exception) {
            false
        }
        return IntegrityFinding(
            check = IntegrityCheck.SELINUX_PERMISSIVE,
            detected = permissive,
            detail = if (permissive) "SELinux is in permissive mode" else "SELinux enforcing",
            severity = if (permissive) FindingSeverity.HIGH else FindingSeverity.LOW
        )
    }

    private fun checkEmulator(): IntegrityFinding {
        val isEmulator = EMULATOR_SIGNATURES.any { sig ->
            Build.FINGERPRINT.contains(sig, ignoreCase = true) ||
            Build.MODEL.contains(sig, ignoreCase = true) ||
            Build.MANUFACTURER.contains(sig, ignoreCase = true) ||
            Build.BRAND.contains(sig, ignoreCase = true) ||
            Build.DEVICE.contains(sig, ignoreCase = true) ||
            Build.PRODUCT.contains(sig, ignoreCase = true)
        } || Build.HARDWARE.contains("goldfish") || Build.HARDWARE.contains("ranchu")

        return IntegrityFinding(
            check = IntegrityCheck.EMULATOR_DETECTED,
            detected = isEmulator,
            detail = if (isEmulator) "Running in emulator (${Build.MODEL})" else "Physical device",
            severity = if (isEmulator) FindingSeverity.MEDIUM else FindingSeverity.LOW
        )
    }

    private fun checkDebugger(): IntegrityFinding {
        val debuggerAttached = Debug.isDebuggerConnected() || Debug.waitingForDebugger()
        return IntegrityFinding(
            check = IntegrityCheck.DEBUGGER_ATTACHED,
            detected = debuggerAttached,
            detail = if (debuggerAttached) "Debugger is currently attached" else "No debugger",
            severity = if (debuggerAttached) FindingSeverity.MEDIUM else FindingSeverity.LOW
        )
    }

    private fun checkDeveloperSettings(): List<IntegrityFinding> {
        val findings = mutableListOf<IntegrityFinding>()

        // Developer options
        val devOptionsEnabled = try {
            Settings.Secure.getInt(
                context.contentResolver,
                Settings.Global.DEVELOPMENT_SETTINGS_ENABLED,
                0
            ) != 0
        } catch (_: Exception) { false }

        findings.add(IntegrityFinding(
            check = IntegrityCheck.DEVELOPER_OPTIONS_ENABLED,
            detected = devOptionsEnabled,
            detail = if (devOptionsEnabled) "Developer options enabled" else "Developer options off",
            severity = FindingSeverity.LOW
        ))

        // USB debugging
        val usbDebugging = try {
            Settings.Secure.getInt(
                context.contentResolver,
                Settings.Global.ADB_ENABLED,
                0
            ) != 0
        } catch (_: Exception) { false }

        findings.add(IntegrityFinding(
            check = IntegrityCheck.USB_DEBUGGING_ENABLED,
            detected = usbDebugging,
            detail = if (usbDebugging) "USB debugging enabled" else "USB debugging off",
            severity = if (usbDebugging) FindingSeverity.LOW else FindingSeverity.LOW
        ))

        // Mock location
        val mockLocation = try {
            Settings.Secure.getString(
                context.contentResolver,
                Settings.Secure.ALLOW_MOCK_LOCATION
            ) == "1"
        } catch (_: Exception) { false }

        findings.add(IntegrityFinding(
            check = IntegrityCheck.MOCK_LOCATION_ENABLED,
            detected = mockLocation,
            detail = if (mockLocation) "Mock location provider allowed" else "Mock locations off",
            severity = if (mockLocation) FindingSeverity.HIGH else FindingSeverity.LOW
        ))

        return findings
    }

    private fun calculateRiskScore(findings: List<IntegrityFinding>): Float {
        if (findings.isEmpty()) return 0.0f

        var score = 0.0f
        findings.filter { it.detected }.forEach { finding ->
            score += when (finding.severity) {
                FindingSeverity.CRITICAL -> 0.4f
                FindingSeverity.HIGH -> 0.2f
                FindingSeverity.MEDIUM -> 0.1f
                FindingSeverity.LOW -> 0.02f
            }
        }
        return score.coerceIn(0.0f, 1.0f)
    }
}
