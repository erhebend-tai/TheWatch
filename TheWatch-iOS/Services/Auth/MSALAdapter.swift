// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         MSALAdapter.swift
// Purpose:      Production adapter implementing MSALAuthPort using Microsoft
//               Authentication Library (MSAL) for iOS. Provides Azure AD B2C
//               authentication including interactive sign-in, silent token
//               renewal, password reset, and profile edit B2C policy flows.
//               Requires MSAL iOS SDK (via SPM or CocoaPods).
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, UIKit, MSAL (Microsoft Authentication Library iOS)
//               SPM: https://github.com/AzureAD/microsoft-authentication-library-for-objc
//               Pod: pod 'MSAL', '~> 1.4'
// Related:      MSALAuthPort.swift (port protocol),
//               MockMSALAdapter.swift (mock for dev/test)
//
// Usage Example:
//   let adapter = MSALAdapter()
//   try await adapter.configure(with: MSALConfiguration(
//       clientId: "your-client-id-from-azure-portal",
//       tenantName: "yourtenantname"
//   ))
//   let result = try await adapter.signIn()
//   // Use result.accessToken for API calls
//   // Use result.userObjectId for user identification
//
// Azure Portal Setup Required:
//   1. Register app in Azure AD B2C tenant
//   2. Add iOS platform redirect URI: msauth.com.thewatch.ios://auth
//   3. Create user flows: B2C_1_signup_signin, B2C_1_password_reset, B2C_1_profile_edit
//   4. Configure API scopes
//   5. Add LSApplicationQueriesSchemes to Info.plist: ["msauthv2", "msauthv3"]
//   6. Add URL scheme to Info.plist: msauth.com.thewatch.ios
//
// Info.plist Requirements:
//   <key>LSApplicationQueriesSchemes</key>
//   <array>
//       <string>msauthv2</string>
//       <string>msauthv3</string>
//   </array>
//   <key>CFBundleURLTypes</key>
//   <array>
//       <dict>
//           <key>CFBundleURLSchemes</key>
//           <array>
//               <string>msauth.com.thewatch.ios</string>
//           </array>
//       </dict>
//   </array>
//
// Potential Additions:
//   - Keychain access group sharing for app extensions
//   - Brokered authentication via Microsoft Authenticator
//   - Conditional Access compliance (Intune)
//   - Continuous Access Evaluation (CAE)
//   - Device code flow for secondary devices
//   - PKCE enforcement verification
//   - Token cache serialization to custom storage
// ============================================================================

import Foundation
import UIKit
// NOTE: Uncomment the following import when MSAL SDK is added to the project.
// import MSAL

/// Production adapter for MSAL / Entra ID (Azure AD B2C) authentication.
///
/// This adapter wraps the Microsoft MSAL iOS SDK and implements the MSALAuthPort
/// protocol. It handles all interactive and silent token flows, B2C policy
/// routing, and account caching.
///
/// **IMPORTANT**: This file requires the MSAL iOS SDK. Add it via Swift Package Manager:
///   URL: https://github.com/AzureAD/microsoft-authentication-library-for-objc
///   Version: 1.4.0+
///
/// Until the MSAL SDK is added, this file uses stubbed implementations that
/// throw `.notConfigured`. The commented-out sections show the real MSAL calls.
final class MSALAdapter: MSALAuthPort, @unchecked Sendable {

    // MARK: - Private State

    private var configuration: MSALConfiguration?

    // NOTE: Uncomment when MSAL SDK is available:
    // private var msalApplication: MSALPublicClientApplication?
    // private var currentAccount: MSALAccount?

    private var _isConfigured = false
    private var _currentUserObjectId: String?

    // MARK: - MSALAuthPort Properties

    var isConfigured: Bool { _isConfigured }

    var hasCachedAccount: Bool {
        // NOTE: Real implementation checks MSAL account cache:
        // return (try? msalApplication?.allAccounts().first) != nil
        return _currentUserObjectId != nil
    }

    var currentUserObjectId: String? { _currentUserObjectId }

    // MARK: - Configuration

    func configure(with configuration: MSALConfiguration) async throws {
        guard !configuration.clientId.isEmpty else {
            throw MSALAuthError.invalidConfiguration(detail: "Client ID is required")
        }
        guard !configuration.tenantName.isEmpty else {
            throw MSALAuthError.invalidConfiguration(detail: "Tenant name is required")
        }

        self.configuration = configuration

        // ── Real MSAL SDK Configuration ──────────────────────────────────
        // Uncomment when MSAL SDK is added:
        //
        // do {
        //     let authorityURL = configuration.defaultPolicy.authorityURL(
        //         tenantName: configuration.tenantName
        //     )
        //
        //     guard let authority = try? MSALB2CAuthority(
        //         url: URL(string: authorityURL)!
        //     ) else {
        //         throw MSALAuthError.invalidConfiguration(
        //             detail: "Invalid authority URL: \(authorityURL)"
        //         )
        //     }
        //
        //     // Build known authorities for all B2C policies
        //     let knownAuthorities = B2CPolicy.allCases.compactMap { policy in
        //         try? MSALB2CAuthority(
        //             url: URL(string: policy.authorityURL(
        //                 tenantName: configuration.tenantName
        //             ))!
        //         )
        //     }
        //
        //     let msalConfig = MSALPublicClientApplicationConfig(
        //         clientId: configuration.clientId,
        //         redirectUri: configuration.redirectUri,
        //         authority: authority
        //     )
        //     msalConfig.knownAuthorities = knownAuthorities
        //
        //     msalApplication = try MSALPublicClientApplication(
        //         configuration: msalConfig
        //     )
        //
        //     _isConfigured = true
        //
        //     // Attempt to restore cached account
        //     if let account = try? msalApplication?.allAccounts().first {
        //         currentAccount = account
        //         _currentUserObjectId = account.identifier
        //     }
        // } catch let error as MSALAuthError {
        //     throw error
        // } catch {
        //     throw MSALAuthError.invalidConfiguration(
        //         detail: error.localizedDescription
        //     )
        // }
        // ─────────────────────────────────────────────────────────────────

        _isConfigured = true
        print("[MSALAdapter] Configured for tenant: \(configuration.tenantName)")
    }

    // MARK: - Sign In

    func signIn() async throws -> MSALAuthResult {
        guard let config = configuration else {
            throw MSALAuthError.notConfigured
        }
        return try await signIn(policy: config.defaultPolicy)
    }

    func signIn(policy: B2CPolicy) async throws -> MSALAuthResult {
        guard _isConfigured, let config = configuration else {
            throw MSALAuthError.notConfigured
        }

        // ── Real MSAL SDK Interactive Sign-In ────────────────────────────
        // Uncomment when MSAL SDK is available:
        //
        // guard let application = msalApplication else {
        //     throw MSALAuthError.notConfigured
        // }
        //
        // let authorityURL = policy.authorityURL(tenantName: config.tenantName)
        // guard let authority = try? MSALB2CAuthority(
        //     url: URL(string: authorityURL)!
        // ) else {
        //     throw MSALAuthError.b2cPolicyNotFound(policyName: policy.rawValue)
        // }
        //
        // return try await withCheckedThrowingContinuation { continuation in
        //     DispatchQueue.main.async {
        //         guard let windowScene = UIApplication.shared.connectedScenes.first
        //             as? UIWindowScene,
        //             let rootVC = windowScene.windows.first?.rootViewController
        //         else {
        //             continuation.resume(throwing: MSALAuthError.signInFailed(
        //                 underlying: "No root view controller available"
        //             ))
        //             return
        //         }
        //
        //         let webviewParameters = MSALWebviewParameters(
        //             authPresentationViewController: rootVC
        //         )
        //
        //         let parameters = MSALInteractiveTokenParameters(
        //             scopes: config.scopes,
        //             webviewParameters: webviewParameters
        //         )
        //         parameters.authority = authority
        //         parameters.promptType = .selectAccount
        //
        //         application.acquireToken(with: parameters) { result, error in
        //             if let error = error as NSError? {
        //                 if error.domain == MSALErrorDomain {
        //                     if error.code == MSALError.userCanceled.rawValue {
        //                         continuation.resume(throwing: MSALAuthError.userCancelled)
        //                         return
        //                     }
        //                     if error.code == MSALError.serverError.rawValue,
        //                        error.userInfo[MSALOAuthErrorKey] as? String
        //                            == "AADB2C90118" {
        //                         // Password reset required - redirect
        //                         continuation.resume(throwing: MSALAuthError.signInFailed(
        //                             underlying: "Password reset required"
        //                         ))
        //                         return
        //                     }
        //                 }
        //                 continuation.resume(throwing: MSALAuthError.signInFailed(
        //                     underlying: error.localizedDescription
        //                 ))
        //                 return
        //             }
        //
        //             guard let result = result else {
        //                 continuation.resume(throwing: MSALAuthError.signInFailed(
        //                     underlying: "No result returned"
        //                 ))
        //                 return
        //             }
        //
        //             self.currentAccount = result.account
        //             self._currentUserObjectId = result.account.identifier
        //
        //             let authResult = MSALAuthResult(
        //                 accessToken: result.accessToken,
        //                 idToken: result.idToken,
        //                 expiresOn: result.expiresOn ?? Date().addingTimeInterval(3600),
        //                 userObjectId: result.account.identifier ?? "",
        //                 displayName: result.account.username,
        //                 email: result.account.username,
        //                 grantedScopes: result.scopes.map { $0 as String },
        //                 policy: policy.rawValue,
        //                 tenantId: result.tenantProfile?.tenantId
        //             )
        //
        //             continuation.resume(returning: authResult)
        //         }
        //     }
        // }
        // ─────────────────────────────────────────────────────────────────

        // Stub: Until MSAL SDK is added, throw notConfigured with guidance
        throw MSALAuthError.signInFailed(
            underlying: "MSAL SDK not yet linked. Add via SPM: "
            + "https://github.com/AzureAD/microsoft-authentication-library-for-objc"
        )
    }

    // MARK: - Silent Token Acquisition

    func acquireTokenSilently() async throws -> MSALAuthResult {
        guard _isConfigured, let config = configuration else {
            throw MSALAuthError.notConfigured
        }

        // ── Real MSAL SDK Silent Acquisition ─────────────────────────────
        // Uncomment when MSAL SDK is available:
        //
        // guard let application = msalApplication,
        //       let account = currentAccount else {
        //     throw MSALAuthError.accountNotFound
        // }
        //
        // let authorityURL = config.defaultPolicy.authorityURL(
        //     tenantName: config.tenantName
        // )
        // guard let authority = try? MSALB2CAuthority(
        //     url: URL(string: authorityURL)!
        // ) else {
        //     throw MSALAuthError.b2cPolicyNotFound(
        //         policyName: config.defaultPolicy.rawValue
        //     )
        // }
        //
        // let parameters = MSALSilentTokenParameters(
        //     scopes: config.scopes,
        //     account: account
        // )
        // parameters.authority = authority
        // parameters.forceRefresh = false
        //
        // return try await withCheckedThrowingContinuation { continuation in
        //     application.acquireTokenSilent(with: parameters) { result, error in
        //         if let error = error as NSError? {
        //             if error.domain == MSALErrorDomain,
        //                error.code == MSALError.interactionRequired.rawValue {
        //                 continuation.resume(
        //                     throwing: MSALAuthError.interactionRequired
        //                 )
        //             } else {
        //                 continuation.resume(throwing: MSALAuthError.silentTokenFailed(
        //                     underlying: error.localizedDescription
        //                 ))
        //             }
        //             return
        //         }
        //
        //         guard let result = result else {
        //             continuation.resume(throwing: MSALAuthError.silentTokenFailed(
        //                 underlying: "No result"
        //             ))
        //             return
        //         }
        //
        //         let authResult = MSALAuthResult(
        //             accessToken: result.accessToken,
        //             idToken: result.idToken,
        //             expiresOn: result.expiresOn ?? Date().addingTimeInterval(3600),
        //             userObjectId: result.account.identifier ?? "",
        //             displayName: result.account.username,
        //             email: result.account.username,
        //             grantedScopes: result.scopes.map { $0 as String },
        //             policy: config.defaultPolicy.rawValue,
        //             tenantId: result.tenantProfile?.tenantId
        //         )
        //
        //         continuation.resume(returning: authResult)
        //     }
        // }
        // ─────────────────────────────────────────────────────────────────

        throw MSALAuthError.silentTokenFailed(
            underlying: "MSAL SDK not yet linked"
        )
    }

    // MARK: - Sign Out

    func signOut() async throws {
        guard _isConfigured else {
            throw MSALAuthError.notConfigured
        }

        // ── Real MSAL SDK Sign Out ───────────────────────────────────────
        // Uncomment when MSAL SDK is available:
        //
        // guard let application = msalApplication,
        //       let account = currentAccount else {
        //     throw MSALAuthError.accountNotFound
        // }
        //
        // return try await withCheckedThrowingContinuation { continuation in
        //     DispatchQueue.main.async {
        //         guard let windowScene = UIApplication.shared.connectedScenes.first
        //             as? UIWindowScene,
        //             let rootVC = windowScene.windows.first?.rootViewController
        //         else {
        //             continuation.resume(throwing: MSALAuthError.signOutFailed(
        //                 underlying: "No root view controller"
        //             ))
        //             return
        //         }
        //
        //         let webviewParameters = MSALWebviewParameters(
        //             authPresentationViewController: rootVC
        //         )
        //         let signoutParameters = MSALSignoutParameters(
        //             webviewParameters: webviewParameters
        //         )
        //         signoutParameters.signoutFromBrowser = true
        //
        //         application.signout(
        //             with: account,
        //             signoutParameters: signoutParameters
        //         ) { success, error in
        //             if let error = error {
        //                 continuation.resume(throwing: MSALAuthError.signOutFailed(
        //                     underlying: error.localizedDescription
        //                 ))
        //             } else {
        //                 self.currentAccount = nil
        //                 self._currentUserObjectId = nil
        //                 continuation.resume(returning: ())
        //             }
        //         }
        //     }
        // }
        // ─────────────────────────────────────────────────────────────────

        _currentUserObjectId = nil
        print("[MSALAdapter] Signed out (stub)")
    }

    // MARK: - Password Reset

    func resetPassword() async throws -> MSALAuthResult {
        return try await signIn(policy: .passwordReset)
    }

    // MARK: - Edit Profile

    func editProfile() async throws -> MSALAuthResult {
        return try await signIn(policy: .profileEdit)
    }
}
