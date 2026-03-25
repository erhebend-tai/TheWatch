// AccountController — REST endpoints for account management, email verification,
// 2FA enrollment, and account status.
//
// Endpoints:
//   GET  /api/account/status                — Get account status (email verified, MFA, etc.)
//   POST /api/account/verify-email          — Send email verification
//   POST /api/account/confirm-email         — Confirm email with code/token
//   POST /api/account/mfa/enroll            — Start MFA enrollment (totp, sms, email)
//   POST /api/account/mfa/confirm           — Complete MFA enrollment with code
//   POST /api/account/mfa/verify            — Verify MFA code during sign-in
//   POST /api/account/mfa/disable           — Disable MFA
//   POST /api/account/password-reset        — Send password reset (AllowAnonymous)
//   DELETE /api/account                     — Delete account (GDPR right to erasure)
//   POST /api/account/disable               — Disable account (admin)
//   POST /api/account/enable                — Enable account (admin)
//
// WAL: All operations delegate to IAuthPort — provider-swappable.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    private readonly IAuthPort _authPort;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IAuthPort authPort, ILogger<AccountController> logger)
    {
        _authPort = authPort;
        _logger = logger;
    }

    private string? GetUid() => User.FindFirst("uid")?.Value;

    // ── Account Status ───────────────────────────────────────────

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var uid = GetUid();
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        var result = await _authPort.GetAccountStatusAsync(uid, ct);
        if (!result.Success) return StatusCode(500, new { error = result.ErrorMessage });

        return Ok(result.Data);
    }

    // ── Email Verification ───────────────────────────────────────

    [HttpPost("verify-email")]
    public async Task<IActionResult> SendEmailVerification(CancellationToken ct)
    {
        var uid = GetUid();
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        var sent = await _authPort.SendEmailVerificationAsync(uid, ct);
        return sent
            ? Ok(new { message = "Verification email sent" })
            : StatusCode(500, new { error = "Failed to send verification email" });
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest request, CancellationToken ct)
    {
        var uid = GetUid();
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        var confirmed = await _authPort.ConfirmEmailVerificationAsync(uid, request.Code, ct);
        return confirmed
            ? Ok(new { message = "Email verified", emailVerified = true })
            : BadRequest(new { error = "Invalid or expired verification code" });
    }

    // ── Multi-Factor Authentication ──────────────────────────────

    [HttpPost("mfa/enroll")]
    public async Task<IActionResult> EnrollMfa([FromBody] MfaEnrollRequest request, CancellationToken ct)
    {
        var uid = GetUid();
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        if (request.Method is not ("totp" or "sms" or "email"))
            return BadRequest(new { error = "Method must be 'totp', 'sms', or 'email'" });

        var result = await _authPort.EnrollMfaAsync(uid, request.Method, request.PhoneNumber, ct);
        if (!result.Success) return BadRequest(new { error = result.ErrorMessage });

        var challenge = result.Data!;
        return Ok(new
        {
            challenge.Method,
            challenge.ChallengeUri,
            challenge.SessionId,
            challenge.BackupCodes
        });
    }

    [HttpPost("mfa/confirm")]
    public async Task<IActionResult> ConfirmMfaEnrollment([FromBody] MfaConfirmRequest request, CancellationToken ct)
    {
        var uid = GetUid();
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        var confirmed = await _authPort.ConfirmMfaEnrollmentAsync(uid, request.SessionId, request.Code, ct);
        return confirmed
            ? Ok(new { message = "MFA enrolled successfully", mfaEnabled = true })
            : BadRequest(new { error = "Invalid code or session expired" });
    }

    [HttpPost("mfa/verify")]
    public async Task<IActionResult> VerifyMfaCode([FromBody] MfaVerifyRequest request, CancellationToken ct)
    {
        var uid = GetUid();
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        var verified = await _authPort.VerifyMfaCodeAsync(uid, request.Code, request.Method, ct);
        return verified
            ? Ok(new { message = "MFA verified", verified = true })
            : Unauthorized(new { error = "Invalid MFA code" });
    }

    [HttpPost("mfa/disable")]
    public async Task<IActionResult> DisableMfa(CancellationToken ct)
    {
        var uid = GetUid();
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        var disabled = await _authPort.DisableMfaAsync(uid, ct);
        return disabled
            ? Ok(new { message = "MFA disabled", mfaEnabled = false })
            : StatusCode(500, new { error = "Failed to disable MFA" });
    }

    // ── Password Reset (public — no auth required) ───────────────

    [HttpPost("password-reset")]
    [AllowAnonymous]
    public async Task<IActionResult> SendPasswordReset([FromBody] PasswordResetRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Email is required" });

        // Always return 200 to prevent email enumeration attacks
        await _authPort.SendPasswordResetAsync(request.Email, ct);
        return Ok(new { message = "If an account exists with that email, a password reset link has been sent" });
    }

    // ── Account Deletion (GDPR Article 17) ───────────────────────

    [HttpDelete]
    public async Task<IActionResult> DeleteAccount(CancellationToken ct)
    {
        var uid = GetUid();
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        _logger.LogWarning("Account deletion requested for {Uid}", uid);
        var deleted = await _authPort.DeleteAccountAsync(uid, ct);
        return deleted
            ? Ok(new { message = "Account deleted" })
            : StatusCode(500, new { error = "Failed to delete account" });
    }

    // ── Admin: Disable/Enable ────────────────────────────────────

    [HttpPost("disable/{uid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DisableAccount(string uid, CancellationToken ct)
    {
        var disabled = await _authPort.DisableAccountAsync(uid, ct);
        return disabled ? Ok(new { message = $"Account {uid} disabled" }) : StatusCode(500);
    }

    [HttpPost("enable/{uid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> EnableAccount(string uid, CancellationToken ct)
    {
        var enabled = await _authPort.EnableAccountAsync(uid, ct);
        return enabled ? Ok(new { message = $"Account {uid} enabled" }) : StatusCode(500);
    }
}

// ── Request DTOs ─────────────────────────────────────────────────

public record ConfirmEmailRequest(string Code);
public record MfaEnrollRequest(string Method, string? PhoneNumber = null);
public record MfaConfirmRequest(string SessionId, string Code);
public record MfaVerifyRequest(string Code, string Method = "totp");
public record PasswordResetRequest(string Email);
