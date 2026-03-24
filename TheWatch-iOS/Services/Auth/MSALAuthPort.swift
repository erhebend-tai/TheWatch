// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         MSALAuthPort.swift
// Purpose:      Protocol (port) defining the contract for MSAL / Entra ID
//               (Azure AD B2C) authentication in TheWatch iOS app.
//               Part of hexagonal architecture: this is the PORT that adapters
//               implement. Supports sign-in, sign-out, token acquisition,
//               silent renewal, and B2C policy-based flows (sign-up, password
//               reset, profile edit).
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation
// Related:      MSALAdapter.swift (production adapter),
//               MockMSALAdapter.swift (mock adapter for previews/tests)
//
// Usage Example:
//   let authPort: MSALAuthPort = MockMSALAdapter()
//   let result = try await authPort.signIn(presentingFrom: nil)
//   print(result.accessToken)
//
// Azure AD B2C Configuration Required (production):
//   - Tenant name:        yourtenantname.onmicrosoft.com
//   - Client ID:          <registered-app-client-id>
//   - Redirect URI:       msauth.com.thewatch.ios://auth
//   - Policies:           B2C_1_signup, B2C_1_signin, B2C_1_password_reset
//   - Scopes:             ["openid", "profile", "offline_access",
//                          "https://yourtenantname.onmicrosoft.com/api/read"]
//
// Potential Additions:
//   - Device code flow for Apple TV / watchOS companion
//   - Conditional Access compliance checks (Intune MAM)
//   - Continuous Access Evaluation (CAE) support
//   - MSAL broker support (Microsoft Authenticator)
// ============================================================================

import Foundation

// MARK: - MSAL Authentication Result

/// Encapsulates the result of an MSAL authentication operation.
/// Maps to MSAL's MSALResult but decoupled for testability.
struct MSALAuthResult: Sendable {
    /// The OAuth 2.0 access token for API calls
    let accessToken: String

    /// The ID token containing user claims (JWT)
    let idToken: String?

    /// Token expiration date
    let expiresOn: Date

    /// The user's unique object ID in Azure AD (oid claim)
    let userObjectId: String

    /// User's display name from the token claims
    let displayName: String?

    /// User's email from the token claims
    let email: String?

    /// The scopes granted by this token
    let grantedScopes: [String]

    /// The B2C policy used for this authentication
    let policy: String?

    /// The tenant ID
    let tenantId: String?
}

// MARK: - MSAL Auth Errors

/// Domain-specific errors for MSAL authentication flows.
enum MSALAuthError: Error, LocalizedError, Sendable {
    case notConfigured
    case signInFailed(underlying: String)
    case signOutFailed(underlying: String)
    case silentTokenFailed(underlying: String)
    case interactionRequired
    case userCancelled
    case networkUnavailable
    case tokenExpired
    case invalidConfiguration(detail: String)
    case b2cPolicyNotFound(policyName: String)
    case accountNotFound
    case brokerNotAvailable

    var errorDescription: String? {
        switch self {
        case .notConfigured:
            return "MSAL has not been configured. Call configure() before authentication."
        case .signInFailed(let underlying):
            return "Sign-in failed: \(underlying)"
        case .signOutFailed(let underlying):
            return "Sign-out failed: \(underlying)"
        case .silentTokenFailed(let underlying):
            return "Silent token acquisition failed: \(underlying)"
        case .interactionRequired:
            return "User interaction is required to complete authentication."
        case .userCancelled:
            return "User cancelled the authentication flow."
        case .networkUnavailable:
            return "Network is unavailable. Please check your connection."
        case .tokenExpired:
            return "Authentication token has expired. Please sign in again."
        case .invalidConfiguration(let detail):
            return "Invalid MSAL configuration: \(detail)"
        case .b2cPolicyNotFound(let policyName):
            return "Azure AD B2C policy '\(policyName)' was not found."
        case .accountNotFound:
            return "No cached account found. Please sign in."
        case .brokerNotAvailable:
            return "Microsoft Authenticator broker is not available on this device."
        }
    }
}

// MARK: - B2C Policy Enum

/// Azure AD B2C user flow policies.
enum B2CPolicy: String, Sendable, CaseIterable {
    case signUpSignIn = "B2C_1_signup_signin"
    case signUp = "B2C_1_signup"
    case signIn = "B2C_1_signin"
    case passwordReset = "B2C_1_password_reset"
    case profileEdit = "B2C_1_profile_edit"

    /// The full authority URL for this policy.
    /// - Parameter tenantName: The Azure AD B2C tenant name (e.g., "yourtenantname")
    /// - Returns: Full authority URL string
    func authorityURL(tenantName: String) -> String {
        "https://\(tenantName).b2clogin.com/\(tenantName).onmicrosoft.com/\(self.rawValue)"
    }
}

// MARK: - MSAL Configuration

/// Configuration object for MSAL initialization.
struct MSALConfiguration: Sendable {
    let clientId: String
    let tenantName: String
    let redirectUri: String
    let scopes: [String]
    let defaultPolicy: B2CPolicy

    /// Default scopes for TheWatch API access
    static let defaultScopes = [
        "openid",
        "profile",
        "offline_access"
    ]

    /// Creates a configuration with TheWatch defaults.
    /// - Parameters:
    ///   - clientId: The registered application client ID
    ///   - tenantName: The Azure AD B2C tenant name
    init(
        clientId: String,
        tenantName: String,
        redirectUri: String = "msauth.com.thewatch.ios://auth",
        scopes: [String] = MSALConfiguration.defaultScopes,
        defaultPolicy: B2CPolicy = .signUpSignIn
    ) {
        self.clientId = clientId
        self.tenantName = tenantName
        self.redirectUri = redirectUri
        self.scopes = scopes
        self.defaultPolicy = defaultPolicy
    }
}

// MARK: - MSALAuthPort Protocol

/// Port (protocol) for Microsoft Authentication Library (MSAL) / Entra ID
/// authentication. Adapters implement this to provide either real Azure AD B2C
/// authentication or mock authentication for development and testing.
///
/// Follows hexagonal (ports & adapters) architecture pattern.
protocol MSALAuthPort: Sendable {

    /// Configure the MSAL client with Azure AD B2C settings.
    /// Must be called before any authentication operations.
    /// - Parameter configuration: The MSAL configuration
    /// - Throws: `MSALAuthError.invalidConfiguration` if settings are invalid
    func configure(with configuration: MSALConfiguration) async throws

    /// Whether MSAL has been configured and is ready for authentication.
    var isConfigured: Bool { get }

    /// Interactive sign-in using the default B2C sign-up/sign-in policy.
    /// Presents the Microsoft authentication web view.
    /// - Returns: Authentication result with tokens and user info
    /// - Throws: `MSALAuthError` on failure
    func signIn() async throws -> MSALAuthResult

    /// Interactive sign-in with a specific B2C policy.
    /// - Parameter policy: The B2C user flow policy to use
    /// - Returns: Authentication result with tokens and user info
    /// - Throws: `MSALAuthError` on failure
    func signIn(policy: B2CPolicy) async throws -> MSALAuthResult

    /// Attempt to silently acquire a token using cached credentials.
    /// Falls back to interactive sign-in if silent acquisition fails.
    /// - Returns: Authentication result with refreshed tokens
    /// - Throws: `MSALAuthError.interactionRequired` if user must re-authenticate
    func acquireTokenSilently() async throws -> MSALAuthResult

    /// Sign out the current user, clearing all cached tokens.
    /// - Throws: `MSALAuthError.signOutFailed` on failure
    func signOut() async throws

    /// Whether there is a currently cached account that may be usable
    /// for silent token acquisition.
    var hasCachedAccount: Bool { get }

    /// The currently signed-in user's object ID, if available.
    var currentUserObjectId: String? { get }

    /// Initiate the password reset B2C flow.
    /// - Returns: Authentication result after successful password reset
    /// - Throws: `MSALAuthError` on failure
    func resetPassword() async throws -> MSALAuthResult

    /// Initiate the profile edit B2C flow.
    /// - Returns: Authentication result with updated claims
    /// - Throws: `MSALAuthError` on failure
    func editProfile() async throws -> MSALAuthResult
}
