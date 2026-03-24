// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         MockMSALAdapter.swift
// Purpose:      Mock adapter implementing MSALAuthPort for development,
//               SwiftUI previews, and unit testing. Simulates Azure AD B2C
//               authentication flows with configurable delays and error states.
//               No real network calls or MSAL SDK dependency required.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, MSALAuthPort.swift
// Related:      MSALAuthPort.swift (port protocol),
//               MSALAdapter.swift (production adapter)
//
// Usage Example:
//   let mock = MockMSALAdapter()
//   try await mock.configure(with: MSALConfiguration(
//       clientId: "test-client-id",
//       tenantName: "testtenantname"
//   ))
//   let result = try await mock.signIn()
//   print(result.displayName)  // "Alex Rivera"
//
// Testing Error States:
//   mock.shouldFailSignIn = true
//   do { try await mock.signIn() } catch { print(error) }
//
// Potential Additions:
//   - Configurable token expiration for testing refresh flows
//   - Multiple mock user profiles for multi-account testing
//   - Delay injection for timeout testing
//   - Token claim customization
// ============================================================================

import Foundation

/// Mock implementation of MSALAuthPort for development and testing.
/// Provides configurable behavior to simulate various authentication scenarios
/// including success, failure, cancellation, and network errors.
final class MockMSALAdapter: MSALAuthPort, @unchecked Sendable {

    // MARK: - Configurable Test Behaviors

    /// Set to `true` to make signIn throw an error
    var shouldFailSignIn = false

    /// Set to `true` to simulate user cancellation
    var shouldCancelSignIn = false

    /// Set to `true` to simulate network unavailable
    var shouldFailNetwork = false

    /// Set to `true` to make silent token acquisition fail
    var shouldFailSilent = false

    /// Simulated network delay in nanoseconds (default 1 second)
    var simulatedDelayNanoseconds: UInt64 = 1_000_000_000

    /// Track how many times each method was called (for test assertions)
    private(set) var signInCallCount = 0
    private(set) var signOutCallCount = 0
    private(set) var silentAcquireCallCount = 0
    private(set) var configureCallCount = 0

    // MARK: - State

    private var _isConfigured = false
    private var _hasCachedAccount = false
    private var _currentUserObjectId: String?
    private var _configuration: MSALConfiguration?

    // MARK: - MSALAuthPort Conformance

    var isConfigured: Bool { _isConfigured }
    var hasCachedAccount: Bool { _hasCachedAccount }
    var currentUserObjectId: String? { _currentUserObjectId }

    func configure(with configuration: MSALConfiguration) async throws {
        configureCallCount += 1

        guard !configuration.clientId.isEmpty else {
            throw MSALAuthError.invalidConfiguration(detail: "Client ID cannot be empty")
        }
        guard !configuration.tenantName.isEmpty else {
            throw MSALAuthError.invalidConfiguration(detail: "Tenant name cannot be empty")
        }

        try await Task.sleep(nanoseconds: 100_000_000) // 0.1s config time
        _configuration = configuration
        _isConfigured = true
    }

    func signIn() async throws -> MSALAuthResult {
        try await signIn(policy: _configuration?.defaultPolicy ?? .signUpSignIn)
    }

    func signIn(policy: B2CPolicy) async throws -> MSALAuthResult {
        signInCallCount += 1

        guard _isConfigured else {
            throw MSALAuthError.notConfigured
        }

        // Simulate network delay
        try await Task.sleep(nanoseconds: simulatedDelayNanoseconds)

        // Check configurable failure states
        if shouldFailNetwork {
            throw MSALAuthError.networkUnavailable
        }
        if shouldCancelSignIn {
            throw MSALAuthError.userCancelled
        }
        if shouldFailSignIn {
            throw MSALAuthError.signInFailed(underlying: "Mock sign-in failure for testing")
        }

        let result = makeMockResult(policy: policy)
        _hasCachedAccount = true
        _currentUserObjectId = result.userObjectId
        return result
    }

    func acquireTokenSilently() async throws -> MSALAuthResult {
        silentAcquireCallCount += 1

        guard _isConfigured else {
            throw MSALAuthError.notConfigured
        }
        guard _hasCachedAccount else {
            throw MSALAuthError.accountNotFound
        }

        try await Task.sleep(nanoseconds: simulatedDelayNanoseconds / 2)

        if shouldFailSilent {
            throw MSALAuthError.interactionRequired
        }

        return makeMockResult(policy: _configuration?.defaultPolicy ?? .signUpSignIn)
    }

    func signOut() async throws {
        signOutCallCount += 1

        guard _isConfigured else {
            throw MSALAuthError.notConfigured
        }

        try await Task.sleep(nanoseconds: 500_000_000)

        _hasCachedAccount = false
        _currentUserObjectId = nil
    }

    func resetPassword() async throws -> MSALAuthResult {
        guard _isConfigured else {
            throw MSALAuthError.notConfigured
        }

        try await Task.sleep(nanoseconds: simulatedDelayNanoseconds)

        if shouldFailSignIn {
            throw MSALAuthError.signInFailed(underlying: "Mock password reset failure")
        }

        return makeMockResult(policy: .passwordReset)
    }

    func editProfile() async throws -> MSALAuthResult {
        guard _isConfigured else {
            throw MSALAuthError.notConfigured
        }

        try await Task.sleep(nanoseconds: simulatedDelayNanoseconds)

        return makeMockResult(policy: .profileEdit)
    }

    // MARK: - Mock Data Factory

    private func makeMockResult(policy: B2CPolicy) -> MSALAuthResult {
        MSALAuthResult(
            accessToken: "mock-access-token-\(UUID().uuidString.prefix(8))",
            idToken: "mock-id-token-\(UUID().uuidString.prefix(8))",
            expiresOn: Date().addingTimeInterval(3600), // 1 hour from now
            userObjectId: "mock-oid-00000000-0000-0000-0000-000000000001",
            displayName: "Alex Rivera",
            email: "alex@example.com",
            grantedScopes: _configuration?.scopes ?? MSALConfiguration.defaultScopes,
            policy: policy.rawValue,
            tenantId: "mock-tenant-\(_configuration?.tenantName ?? "test")"
        )
    }

    // MARK: - Test Helpers

    /// Reset all counters and state for test isolation
    func reset() {
        signInCallCount = 0
        signOutCallCount = 0
        silentAcquireCallCount = 0
        configureCallCount = 0
        shouldFailSignIn = false
        shouldCancelSignIn = false
        shouldFailNetwork = false
        shouldFailSilent = false
        _isConfigured = false
        _hasCachedAccount = false
        _currentUserObjectId = nil
        _configuration = nil
    }
}
