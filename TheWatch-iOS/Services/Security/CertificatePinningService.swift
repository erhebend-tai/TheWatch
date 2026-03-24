// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         CertificatePinningService.swift
// Purpose:      Implements TLS certificate pinning using Subject Public Key
//               Info (SPKI) hash validation. Provides a URLSession delegate
//               that validates server certificates against a set of pinned
//               SHA-256 SPKI hashes, preventing MITM attacks even if a CA
//               is compromised. Supports pin rotation with backup pins.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, Security, CryptoKit, CommonCrypto
// Related:      NetworkMonitor.swift (connectivity checks),
//               MSALAdapter.swift (API calls use pinned sessions)
//
// Usage Example:
//   let pinningService = CertificatePinningService(
//       pinnedDomains: [
//           PinnedDomain(
//               hostname: "api.thewatch.com",
//               spkiHashes: [
//                   "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=", // Primary
//                   "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC="  // Backup
//               ]
//           )
//       ]
//   )
//   let session = pinningService.createPinnedSession()
//   let (data, _) = try await session.data(from: apiURL)
//
// How to Generate SPKI Hashes:
//   openssl s_client -connect api.thewatch.com:443 -servername api.thewatch.com \
//     < /dev/null 2>/dev/null | openssl x509 -pubkey -noout | \
//     openssl pkey -pubin -outform DER | openssl dgst -sha256 -binary | \
//     openssl enc -base64
//
// Security Considerations:
//   - Always include at least 2 pins (primary + backup) to avoid lockout
//   - Backup pin should be from a different CA or a pre-generated key
//   - Pin rotation: add new pin, deploy app update, wait, remove old pin
//   - Consider including intermediate CA pins for broader compatibility
//   - This implementation pins SPKI (not full cert) for rotation flexibility
//   - Pinning is bypassed in DEBUG builds to allow Charles/Proxyman debugging
//
// Potential Additions:
//   - Certificate Transparency (CT) log verification
//   - OCSP stapling validation
//   - Pin expiration dates with automatic fallback
//   - Remote pin update via signed configuration
//   - Reporting endpoint for pin validation failures (RFC 7469)
//   - Mutual TLS (mTLS) client certificate support
//   - Network Extension integration for system-wide pinning
// ============================================================================

import Foundation
import Security
import CommonCrypto

// MARK: - Pinned Domain Configuration

/// Configuration for a domain's pinned SPKI hashes.
struct PinnedDomain: Sendable {
    /// The hostname to pin (e.g., "api.thewatch.com")
    let hostname: String

    /// SHA-256 hashes of the Subject Public Key Info (SPKI), base64-encoded.
    /// Must include at least 2: primary and backup.
    let spkiHashes: [String]

    /// Whether to include subdomains (e.g., "*.api.thewatch.com")
    let includeSubdomains: Bool

    init(
        hostname: String,
        spkiHashes: [String],
        includeSubdomains: Bool = false
    ) {
        self.hostname = hostname
        self.spkiHashes = spkiHashes
        self.includeSubdomains = includeSubdomains
    }
}

// MARK: - Pin Validation Result

/// Result of a certificate pin validation.
enum PinValidationResult: Sendable {
    case success
    case pinMismatch(hostname: String, receivedHash: String)
    case noPinsConfigured(hostname: String)
    case certificateExtractionFailed
    case domainNotPinned(hostname: String)

    var isValid: Bool {
        if case .success = self { return true }
        // Domains not in the pin list are allowed through (pin only configured domains)
        if case .domainNotPinned = self { return true }
        return false
    }
}

// MARK: - Certificate Pinning Service

/// Service that provides URLSession-level certificate pinning using SPKI hash
/// validation. Create an instance with your pinned domains, then use
/// `createPinnedSession()` to get a URLSession that validates server certs.
final class CertificatePinningService: NSObject, URLSessionDelegate, @unchecked Sendable {

    // MARK: - Properties

    /// The set of domains with pinned certificates
    private let pinnedDomains: [PinnedDomain]

    /// Callback for pin validation failures (for reporting/logging)
    var onPinValidationFailure: ((PinValidationResult) -> Void)?

    /// Whether pinning is enabled (disabled in DEBUG for proxy debugging)
    private let isPinningEnabled: Bool

    // MARK: - ASN.1 Header for RSA 2048 SPKI

    /// ASN.1 header prepended to the public key before hashing.
    /// This is the DER-encoded header for RSA 2048-bit keys.
    /// Different key types (EC, RSA 4096) need different headers.
    private static let rsa2048Asn1Header: [UInt8] = [
        0x30, 0x82, 0x01, 0x22, 0x30, 0x0d, 0x06, 0x09,
        0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01,
        0x01, 0x05, 0x00, 0x03, 0x82, 0x01, 0x0f, 0x00
    ]

    /// ASN.1 header for EC P-256 keys
    private static let ecDsaSecp256r1Asn1Header: [UInt8] = [
        0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2a, 0x86,
        0x48, 0xce, 0x3d, 0x02, 0x01, 0x06, 0x08, 0x2a,
        0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07, 0x03,
        0x42, 0x00
    ]

    // MARK: - Initialization

    /// Create a certificate pinning service.
    /// - Parameter pinnedDomains: Domains with their pinned SPKI hashes.
    ///   Each domain should have at least 2 pins (primary + backup).
    init(pinnedDomains: [PinnedDomain]) {
        self.pinnedDomains = pinnedDomains

        #if DEBUG
        // Disable pinning in debug builds to allow proxy debugging
        self.isPinningEnabled = false
        print("[CertPinning] WARNING: Certificate pinning DISABLED in DEBUG build")
        #else
        self.isPinningEnabled = true
        #endif

        super.init()
    }

    // MARK: - Session Factory

    /// Create a URLSession with certificate pinning enabled.
    /// - Parameter configuration: Optional URLSessionConfiguration. Defaults to .default.
    /// - Returns: A URLSession that validates server certificates against pinned hashes
    func createPinnedSession(
        configuration: URLSessionConfiguration = .default
    ) -> URLSession {
        return URLSession(
            configuration: configuration,
            delegate: self,
            delegateQueue: nil
        )
    }

    // MARK: - URLSessionDelegate (Certificate Validation)

    func urlSession(
        _ session: URLSession,
        didReceive challenge: URLAuthenticationChallenge,
        completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void
    ) {
        guard challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              let serverTrust = challenge.protectionSpace.serverTrust else {
            completionHandler(.performDefaultHandling, nil)
            return
        }

        let hostname = challenge.protectionSpace.host

        // If pinning is disabled (DEBUG), allow all
        guard isPinningEnabled else {
            completionHandler(.useCredential, URLCredential(trust: serverTrust))
            return
        }

        // Find pin configuration for this hostname
        guard let domainConfig = findPinConfig(for: hostname) else {
            // Domain not in pin list - allow default handling
            completionHandler(.performDefaultHandling, nil)
            return
        }

        // Validate the certificate chain
        let result = validateCertificateChain(
            serverTrust: serverTrust,
            hostname: hostname,
            pinnedHashes: domainConfig.spkiHashes
        )

        if result.isValid {
            completionHandler(.useCredential, URLCredential(trust: serverTrust))
        } else {
            print("[CertPinning] PIN VALIDATION FAILED for \(hostname): \(result)")
            onPinValidationFailure?(result)
            completionHandler(.cancelAuthenticationChallenge, nil)
        }
    }

    // MARK: - Pin Lookup

    private func findPinConfig(for hostname: String) -> PinnedDomain? {
        // Exact match first
        if let exact = pinnedDomains.first(where: { $0.hostname == hostname }) {
            return exact
        }

        // Subdomain match
        for domain in pinnedDomains where domain.includeSubdomains {
            if hostname.hasSuffix("." + domain.hostname) {
                return domain
            }
        }

        return nil
    }

    // MARK: - Certificate Chain Validation

    private func validateCertificateChain(
        serverTrust: SecTrust,
        hostname: String,
        pinnedHashes: [String]
    ) -> PinValidationResult {
        // Evaluate the trust (standard CA validation first)
        var error: CFError?
        guard SecTrustEvaluateWithError(serverTrust, &error) else {
            print("[CertPinning] Trust evaluation failed: \(error?.localizedDescription ?? "unknown")")
            return .certificateExtractionFailed
        }

        // Check each certificate in the chain against our pins
        let certCount = SecTrustGetCertificateCount(serverTrust)

        for index in 0..<certCount {
            guard let certificate = SecTrustCopyCertificateChain(serverTrust)?
                .takeRetainedValue() as? [SecCertificate],
                  index < certificate.count else {
                continue
            }

            let cert = certificate[index]
            if let spkiHash = extractSPKIHash(from: cert) {
                if pinnedHashes.contains(spkiHash) {
                    return .success
                } else {
                    // Log but continue checking the chain
                    print("[CertPinning] Hash mismatch at chain index \(index): \(spkiHash)")
                }
            }
        }

        // Fallback: try the leaf certificate directly
        if let leafCertChain = SecTrustCopyCertificateChain(serverTrust) as? [SecCertificate],
           let leafCert = leafCertChain.first,
           let leafHash = extractSPKIHash(from: leafCert) {
            if pinnedHashes.contains(leafHash) {
                return .success
            }
            return .pinMismatch(hostname: hostname, receivedHash: leafHash)
        }

        return .certificateExtractionFailed
    }

    // MARK: - SPKI Hash Extraction

    /// Extract the SHA-256 hash of the Subject Public Key Info from a certificate.
    /// - Parameter certificate: The SecCertificate to extract from
    /// - Returns: Base64-encoded SHA-256 hash of the SPKI, or nil on failure
    private func extractSPKIHash(from certificate: SecCertificate) -> String? {
        guard let publicKey = SecCertificateCopyKey(certificate) else {
            return nil
        }

        var error: Unmanaged<CFError>?
        guard let publicKeyData = SecKeyCopyExternalRepresentation(publicKey, &error) as Data? else {
            return nil
        }

        // Determine the key type and select the appropriate ASN.1 header
        let keyAttributes = SecKeyCopyAttributes(publicKey) as? [String: Any]
        let keyType = keyAttributes?[kSecAttrKeyType as String] as? String
        let keySize = keyAttributes?[kSecAttrKeySizeInBits as String] as? Int

        var headerBytes: [UInt8]
        if keyType == (kSecAttrKeyTypeEC as String) {
            headerBytes = Self.ecDsaSecp256r1Asn1Header
        } else {
            // Default to RSA 2048
            headerBytes = Self.rsa2048Asn1Header
        }

        // Construct SPKI: ASN.1 header + public key data
        var spkiData = Data(headerBytes)
        spkiData.append(publicKeyData)

        // SHA-256 hash
        var hash = [UInt8](repeating: 0, count: Int(CC_SHA256_DIGEST_LENGTH))
        spkiData.withUnsafeBytes { buffer in
            _ = CC_SHA256(buffer.baseAddress, CC_LONG(spkiData.count), &hash)
        }

        // Base64 encode
        return Data(hash).base64EncodedString()
    }

    // MARK: - Utility

    /// Validate a single URL's certificate against pins (for one-off checks).
    /// - Parameter url: The URL to validate
    /// - Returns: The validation result
    func validateURL(_ url: URL) async -> PinValidationResult {
        guard let host = url.host else {
            return .certificateExtractionFailed
        }

        guard let domainConfig = findPinConfig(for: host) else {
            return .domainNotPinned(hostname: host)
        }

        // Use the pinned session to make a HEAD request
        let session = createPinnedSession()
        var request = URLRequest(url: url)
        request.httpMethod = "HEAD"

        do {
            let (_, response) = try await session.data(for: request)
            if let httpResponse = response as? HTTPURLResponse,
               httpResponse.statusCode < 500 {
                return .success
            }
            return .certificateExtractionFailed
        } catch {
            return .pinMismatch(hostname: host, receivedHash: "validation-failed")
        }
    }
}

// MARK: - Default Pin Configuration

extension CertificatePinningService {
    /// Factory method creating a CertificatePinningService with TheWatch's
    /// production API pins. Update these hashes when rotating certificates.
    ///
    /// To generate new pin hashes, run:
    /// ```bash
    /// openssl s_client -connect api.thewatch.com:443 -servername api.thewatch.com \
    ///   < /dev/null 2>/dev/null | openssl x509 -pubkey -noout | \
    ///   openssl pkey -pubin -outform DER | openssl dgst -sha256 -binary | \
    ///   openssl enc -base64
    /// ```
    static func theWatchDefault() -> CertificatePinningService {
        return CertificatePinningService(pinnedDomains: [
            PinnedDomain(
                hostname: "api.thewatch.com",
                spkiHashes: [
                    // PRIMARY: Replace with actual SPKI hash of api.thewatch.com leaf cert
                    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    // BACKUP: Replace with backup CA or pre-generated key hash
                    "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB="
                ],
                includeSubdomains: true
            ),
            PinnedDomain(
                hostname: "auth.thewatch.com",
                spkiHashes: [
                    // PRIMARY: Replace with actual SPKI hash
                    "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC=",
                    // BACKUP: Replace with backup hash
                    "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD="
                ],
                includeSubdomains: false
            )
        ])
    }
}
