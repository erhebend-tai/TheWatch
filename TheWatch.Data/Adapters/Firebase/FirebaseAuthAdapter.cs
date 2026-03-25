// =============================================================================
// FirebaseAuthAdapter — IAuthPort implementation using Firebase Admin SDK.
// =============================================================================
// Validates Firebase ID tokens server-side using Google's public key verification.
// Supports custom claims, email verification, MFA enrollment, and account management.
//
// Firebase MFA note: Firebase Auth supports TOTP MFA natively (2024+). The Admin SDK
// can enroll and verify second factors. SMS MFA requires the client-side SDK.
// For providers without native MFA, use custom claims + server-side TOTP validation.
//
// Requires: FirebaseAdmin NuGet in TheWatch.Data.csproj
// Configuration: DatabaseSettings section in appsettings.json
//
// WAL: [WAL-AUTH-FIREBASE] prefix for all log messages.
// =============================================================================

using System.Security.Cryptography;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Firebase;

public class FirebaseAuthAdapter : IAuthPort
{
    private readonly FirebaseAuth _auth;
    private readonly ILogger<FirebaseAuthAdapter> _logger;

    public string ProviderName => "firebase";

    public FirebaseAuthAdapter(FirebaseApp firebaseApp, ILogger<FirebaseAuthAdapter> logger)
    {
        _auth = FirebaseAuth.GetAuth(firebaseApp);
        _logger = logger;
        _logger.LogInformation("[WAL-AUTH-FIREBASE] Initialized Firebase Auth adapter");
    }

    // ── Token Validation ─────────────────────────────────────────

    public async Task<StorageResult<WatchUserClaims>> ValidateTokenAsync(
        string idToken, CancellationToken ct = default)
    {
        try
        {
            var decoded = await _auth.VerifyIdTokenAsync(idToken, ct);

            var claims = new WatchUserClaims
            {
                Uid = decoded.Uid,
                Email = decoded.Claims.TryGetValue("email", out var email) ? email?.ToString() ?? "" : "",
                EmailVerified = decoded.Claims.TryGetValue("email_verified", out var ev) && ev is true,
                DisplayName = decoded.Claims.TryGetValue("name", out var name) ? name?.ToString() : null,
                PhotoUrl = decoded.Claims.TryGetValue("picture", out var photo) ? photo?.ToString() : null,
                PhoneNumber = decoded.Claims.TryGetValue("phone_number", out var phone) ? phone?.ToString() : null,
                Provider = "firebase",
                MfaEnabled = decoded.Claims.TryGetValue("mfa_enabled", out var mfa) && mfa is true,
                TokenIssuedAt = DateTimeOffset.FromUnixTimeSeconds(decoded.IssuedAtTimeSeconds).UtcDateTime,
                TokenExpiresAt = DateTimeOffset.FromUnixTimeSeconds(decoded.ExpirationTimeSeconds).UtcDateTime,
                Roles = ExtractRoles(decoded.Claims),
                CustomClaims = ExtractCustomClaims(decoded.Claims)
            };

            _logger.LogDebug("[WAL-AUTH-FIREBASE] Validated token for {Uid} ({Email}), emailVerified={Verified}, mfa={Mfa}",
                claims.Uid, claims.Email, claims.EmailVerified, claims.MfaEnabled);

            return StorageResult<WatchUserClaims>.Ok(claims);
        }
        catch (FirebaseAuthException ex)
        {
            _logger.LogWarning(ex, "[WAL-AUTH-FIREBASE] Token validation failed: {Code}", ex.AuthErrorCode);
            return StorageResult<WatchUserClaims>.Fail($"Firebase token invalid: {ex.AuthErrorCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Unexpected error validating token");
            return StorageResult<WatchUserClaims>.Fail($"Auth validation error: {ex.Message}");
        }
    }

    public async Task<bool> RevokeTokensAsync(string uid, CancellationToken ct = default)
    {
        try
        {
            await _auth.RevokeRefreshTokensAsync(uid, ct);
            _logger.LogInformation("[WAL-AUTH-FIREBASE] Revoked tokens for user {Uid}", uid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to revoke tokens for {Uid}", uid);
            return false;
        }
    }

    // ── Claims & Roles ───────────────────────────────────────────

    public async Task<bool> SetCustomClaimsAsync(string uid, Dictionary<string, object> claims, CancellationToken ct = default)
    {
        try
        {
            await _auth.SetCustomUserClaimsAsync(uid, claims, ct);
            _logger.LogInformation("[WAL-AUTH-FIREBASE] Set custom claims for {Uid}: {Claims}",
                uid, string.Join(", ", claims.Keys));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to set custom claims for {Uid}", uid);
            return false;
        }
    }

    // ── Account Status ───────────────────────────────────────────

    public async Task<StorageResult<AccountStatus>> GetAccountStatusAsync(
        string uid, CancellationToken ct = default)
    {
        try
        {
            var user = await _auth.GetUserAsync(uid, ct);
            var mfaEnabled = user.CustomClaims?.TryGetValue("mfa_enabled", out var mfa) == true && mfa is true;
            var mfaMethods = new List<string>();
            if (user.CustomClaims?.TryGetValue("mfa_methods", out var methods) == true && methods is IEnumerable<object> methodList)
                mfaMethods = methodList.Select(m => m.ToString() ?? "").Where(m => m.Length > 0).ToList();

            return StorageResult<AccountStatus>.Ok(new AccountStatus
            {
                Uid = user.Uid,
                EmailVerified = user.EmailVerified,
                PhoneVerified = !string.IsNullOrEmpty(user.PhoneNumber),
                MfaEnabled = mfaEnabled,
                MfaMethods = mfaMethods,
                LastSignIn = user.UserMetaData?.LastSignInTimestamp,
                CreatedAt = user.UserMetaData?.CreationTimestamp ?? DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to get account status for {Uid}", uid);
            return StorageResult<AccountStatus>.Fail($"Failed to get account status: {ex.Message}");
        }
    }

    // ── Email Verification ───────────────────────────────────────

    public async Task<bool> SendEmailVerificationAsync(string uid, CancellationToken ct = default)
    {
        try
        {
            // Firebase Admin SDK generates an email verification link; the app sends it.
            // In production, configure Firebase email templates in the console.
            var link = await _auth.GenerateEmailVerificationLinkAsync(
                (await _auth.GetUserAsync(uid, ct)).Email, null, ct);
            _logger.LogInformation("[WAL-AUTH-FIREBASE] Generated email verification link for {Uid}", uid);
            // Link delivery happens via Firebase's built-in email service or your own SMTP.
            // Firebase client SDK's sendEmailVerification() is the typical path.
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to send email verification for {Uid}", uid);
            return false;
        }
    }

    public Task<bool> ConfirmEmailVerificationAsync(string uid, string code, CancellationToken ct = default)
    {
        // Firebase handles email verification client-side via the link.
        // The server can't confirm with a code — the client SDK applies the oobCode.
        _logger.LogDebug("[WAL-AUTH-FIREBASE] Email verification is client-side for Firebase");
        return Task.FromResult(false);
    }

    // ── Multi-Factor Authentication ──────────────────────────────

    public async Task<StorageResult<MfaEnrollmentChallenge>> EnrollMfaAsync(
        string uid, string method, string? phoneNumber = null, CancellationToken ct = default)
    {
        try
        {
            if (method == "totp")
            {
                // Generate TOTP secret server-side, store in custom claims
                var secret = GenerateTotpSecret();
                var user = await _auth.GetUserAsync(uid, ct);
                var email = user.Email ?? uid;

                var otpauthUri = $"otpauth://totp/TheWatch:{email}?secret={secret}&issuer=TheWatch&digits=6&period=30";
                var sessionId = Guid.NewGuid().ToString("N");
                var backupCodes = Enumerable.Range(0, 10)
                    .Select(_ => RandomNumberGenerator.GetInt32(10000000, 99999999).ToString())
                    .ToList();

                // Store pending enrollment in custom claims (completed on confirm)
                await _auth.SetCustomUserClaimsAsync(uid, new Dictionary<string, object>
                {
                    ["mfa_pending_session"] = sessionId,
                    ["mfa_pending_secret"] = secret,
                    ["mfa_pending_method"] = method,
                    ["mfa_backup_codes"] = backupCodes
                }, ct);

                _logger.LogInformation("[WAL-AUTH-FIREBASE] Started TOTP enrollment for {Uid}", uid);

                return StorageResult<MfaEnrollmentChallenge>.Ok(new MfaEnrollmentChallenge
                {
                    Method = "totp",
                    ChallengeUri = otpauthUri,
                    SessionId = sessionId,
                    BackupCodes = backupCodes
                });
            }

            if (method == "sms")
            {
                if (string.IsNullOrEmpty(phoneNumber))
                    return StorageResult<MfaEnrollmentChallenge>.Fail("Phone number required for SMS MFA");

                // Firebase MFA SMS is client-side (PhoneMultiFactorGenerator).
                // Server returns the masked number and a session ID for the client to use.
                var masked = phoneNumber.Length > 4
                    ? new string('*', phoneNumber.Length - 4) + phoneNumber[^4..]
                    : phoneNumber;
                var sessionId = Guid.NewGuid().ToString("N");

                await _auth.SetCustomUserClaimsAsync(uid, new Dictionary<string, object>
                {
                    ["mfa_pending_session"] = sessionId,
                    ["mfa_pending_method"] = "sms",
                    ["mfa_pending_phone"] = phoneNumber
                }, ct);

                _logger.LogInformation("[WAL-AUTH-FIREBASE] Started SMS MFA enrollment for {Uid} ({Masked})", uid, masked);

                return StorageResult<MfaEnrollmentChallenge>.Ok(new MfaEnrollmentChallenge
                {
                    Method = "sms",
                    ChallengeUri = masked,
                    SessionId = sessionId
                });
            }

            return StorageResult<MfaEnrollmentChallenge>.Fail($"Unsupported MFA method: {method}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to enroll MFA for {Uid}", uid);
            return StorageResult<MfaEnrollmentChallenge>.Fail($"MFA enrollment error: {ex.Message}");
        }
    }

    public async Task<bool> ConfirmMfaEnrollmentAsync(
        string uid, string sessionId, string code, CancellationToken ct = default)
    {
        try
        {
            var user = await _auth.GetUserAsync(uid, ct);
            var claims = user.CustomClaims ?? new Dictionary<string, object>();

            if (!claims.TryGetValue("mfa_pending_session", out var pending) || pending?.ToString() != sessionId)
            {
                _logger.LogWarning("[WAL-AUTH-FIREBASE] Invalid MFA session for {Uid}", uid);
                return false;
            }

            var method = claims.TryGetValue("mfa_pending_method", out var m) ? m?.ToString() : null;

            if (method == "totp" && claims.TryGetValue("mfa_pending_secret", out var secret))
            {
                if (!VerifyTotpCode(secret.ToString()!, code))
                {
                    _logger.LogWarning("[WAL-AUTH-FIREBASE] Invalid TOTP code for {Uid}", uid);
                    return false;
                }
            }
            // SMS verification happens client-side in Firebase; we trust the session here.

            // Mark MFA as enabled
            var existingMethods = new List<string>();
            if (claims.TryGetValue("mfa_methods", out var existing) && existing is IEnumerable<object> list)
                existingMethods = list.Select(x => x.ToString() ?? "").ToList();
            if (!existingMethods.Contains(method!))
                existingMethods.Add(method!);

            var newClaims = new Dictionary<string, object>
            {
                ["mfa_enabled"] = true,
                ["mfa_methods"] = existingMethods
            };

            // Keep TOTP secret and backup codes for verification; clear pending state
            if (method == "totp" && claims.TryGetValue("mfa_pending_secret", out var totpSecret))
                newClaims["mfa_totp_secret"] = totpSecret;
            if (claims.TryGetValue("mfa_backup_codes", out var backup))
                newClaims["mfa_backup_codes"] = backup;

            await _auth.SetCustomUserClaimsAsync(uid, newClaims, ct);

            _logger.LogInformation("[WAL-AUTH-FIREBASE] MFA enrollment confirmed for {Uid} via {Method}", uid, method);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to confirm MFA enrollment for {Uid}", uid);
            return false;
        }
    }

    public async Task<bool> VerifyMfaCodeAsync(
        string uid, string code, string method, CancellationToken ct = default)
    {
        try
        {
            var user = await _auth.GetUserAsync(uid, ct);
            var claims = user.CustomClaims ?? new Dictionary<string, object>();

            if (method == "totp")
            {
                if (!claims.TryGetValue("mfa_totp_secret", out var secret))
                    return false;
                return VerifyTotpCode(secret.ToString()!, code);
            }

            if (method == "backup")
            {
                if (!claims.TryGetValue("mfa_backup_codes", out var codesObj) || codesObj is not IEnumerable<object> codes)
                    return false;
                var codeList = codes.Select(c => c.ToString() ?? "").ToList();
                if (!codeList.Contains(code))
                    return false;

                // Remove used backup code
                codeList.Remove(code);
                await _auth.SetCustomUserClaimsAsync(uid, new Dictionary<string, object>
                {
                    ["mfa_backup_codes"] = codeList,
                    ["mfa_enabled"] = true,
                    ["mfa_methods"] = claims.TryGetValue("mfa_methods", out var m) ? m : new List<string>()
                }, ct);

                _logger.LogInformation("[WAL-AUTH-FIREBASE] Backup code used for {Uid}, {Remaining} remaining",
                    uid, codeList.Count);
                return true;
            }

            // SMS MFA verification happens client-side in Firebase
            _logger.LogDebug("[WAL-AUTH-FIREBASE] SMS MFA verification is client-side for Firebase");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to verify MFA code for {Uid}", uid);
            return false;
        }
    }

    public async Task<bool> DisableMfaAsync(string uid, CancellationToken ct = default)
    {
        try
        {
            await _auth.SetCustomUserClaimsAsync(uid, new Dictionary<string, object>
            {
                ["mfa_enabled"] = false,
                ["mfa_methods"] = new List<string>()
            }, ct);
            _logger.LogInformation("[WAL-AUTH-FIREBASE] Disabled MFA for {Uid}", uid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to disable MFA for {Uid}", uid);
            return false;
        }
    }

    // ── Account Management ───────────────────────────────────────

    public async Task<bool> SendPasswordResetAsync(string email, CancellationToken ct = default)
    {
        try
        {
            await _auth.GeneratePasswordResetLinkAsync(email, null, ct);
            _logger.LogInformation("[WAL-AUTH-FIREBASE] Password reset link generated for {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to send password reset for {Email}", email);
            return false;
        }
    }

    public async Task<bool> DeleteAccountAsync(string uid, CancellationToken ct = default)
    {
        try
        {
            await _auth.DeleteUserAsync(uid, ct);
            _logger.LogInformation("[WAL-AUTH-FIREBASE] Deleted account for {Uid}", uid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to delete account for {Uid}", uid);
            return false;
        }
    }

    public async Task<bool> DisableAccountAsync(string uid, CancellationToken ct = default)
    {
        try
        {
            await _auth.UpdateUserAsync(new UserRecordArgs { Uid = uid, Disabled = true }, ct);
            _logger.LogInformation("[WAL-AUTH-FIREBASE] Disabled account for {Uid}", uid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to disable account for {Uid}", uid);
            return false;
        }
    }

    public async Task<bool> EnableAccountAsync(string uid, CancellationToken ct = default)
    {
        try
        {
            await _auth.UpdateUserAsync(new UserRecordArgs { Uid = uid, Disabled = false }, ct);
            _logger.LogInformation("[WAL-AUTH-FIREBASE] Enabled account for {Uid}", uid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to enable account for {Uid}", uid);
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static List<string> ExtractRoles(IReadOnlyDictionary<string, object> claims)
    {
        if (claims.TryGetValue("roles", out var rolesObj) && rolesObj is IEnumerable<object> rolesList)
            return rolesList.Select(r => r.ToString() ?? "").Where(r => r.Length > 0).ToList();
        return new List<string>();
    }

    private static Dictionary<string, string> ExtractCustomClaims(IReadOnlyDictionary<string, object> claims)
    {
        var standardKeys = new HashSet<string>
        {
            "iss", "aud", "auth_time", "user_id", "sub", "iat", "exp",
            "email", "email_verified", "name", "picture", "phone_number",
            "firebase", "roles", "mfa_enabled", "mfa_methods",
            "mfa_totp_secret", "mfa_backup_codes",
            "mfa_pending_session", "mfa_pending_secret", "mfa_pending_method", "mfa_pending_phone"
        };

        return claims
            .Where(c => !standardKeys.Contains(c.Key) && c.Value is not null)
            .ToDictionary(c => c.Key, c => c.Value.ToString() ?? "");
    }

    /// <summary>Generate a 20-byte base32-encoded TOTP secret.</summary>
    private static string GenerateTotpSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return Base32Encode(bytes);
    }

    /// <summary>Verify a 6-digit TOTP code against a base32 secret (RFC 6238, 30s window).</summary>
    private static bool VerifyTotpCode(string base32Secret, string code)
    {
        if (code.Length != 6 || !int.TryParse(code, out _))
            return false;

        var secret = Base32Decode(base32Secret);
        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        // Check current window and ±1 for clock drift
        for (var i = -1; i <= 1; i++)
        {
            var step = timeStep + i;
            var stepBytes = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian) Array.Reverse(stepBytes);
            var padded = new byte[8];
            Array.Copy(stepBytes, 0, padded, 8 - stepBytes.Length, stepBytes.Length);

            using var hmac = new HMACSHA1(secret);
            var hash = hmac.ComputeHash(padded);
            var offset = hash[^1] & 0x0F;
            var truncated = ((hash[offset] & 0x7F) << 24)
                          | ((hash[offset + 1] & 0xFF) << 16)
                          | ((hash[offset + 2] & 0xFF) << 8)
                          | (hash[offset + 3] & 0xFF);
            var otpCode = (truncated % 1000000).ToString("D6");

            if (otpCode == code) return true;
        }

        return false;
    }

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new char[(data.Length * 8 + 4) / 5];
        var buffer = 0; var bitsLeft = 0; var index = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                result[index++] = alphabet[(buffer >> (bitsLeft - 5)) & 0x1F];
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0)
            result[index++] = alphabet[(buffer << (5 - bitsLeft)) & 0x1F];
        return new string(result, 0, index);
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        var buffer = 0; var bitsLeft = 0;
        foreach (var c in base32.ToUpperInvariant())
        {
            var val = alphabet.IndexOf(c);
            if (val < 0) continue;
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }
        return output.ToArray();
    }
}
