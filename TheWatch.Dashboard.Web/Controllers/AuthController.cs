// =============================================================================
// AuthController — Session management for Firebase Auth in Blazor SSR.
// =============================================================================
// Handles the token-to-cookie exchange:
//   POST /auth/session   → Validates Firebase ID token, sets auth cookie
//   POST /auth/signout   → Clears auth cookie
//   GET  /auth/me        → Returns current user claims (for JS interop)
//
// This runs in Dashboard.Web (not Dashboard.Api) because the cookie
// must be set on the same origin as the Blazor app.
// =============================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Web.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthPort _authPort;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthPort authPort, ILogger<AuthController> logger)
    {
        _authPort = authPort;
        _logger = logger;
    }

    /// <summary>
    /// Exchange a Firebase ID token for a session cookie.
    /// Called by the Firebase JS SDK after successful sign-in.
    /// </summary>
    [HttpPost("session")]
    public async Task<IActionResult> CreateSession([FromBody] SessionRequest request)
    {
        if (string.IsNullOrEmpty(request.IdToken))
            return BadRequest(new { error = "Missing idToken" });

        var result = await _authPort.ValidateTokenAsync(request.IdToken);
        if (!result.Success)
        {
            _logger.LogWarning("Auth session creation failed: {Error}", result.ErrorMessage);
            return Unauthorized(new { error = result.ErrorMessage });
        }

        var claims = result.Data!;
        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, claims.Uid));
        identity.AddClaim(new Claim(ClaimTypes.Email, claims.Email));
        if (!string.IsNullOrEmpty(claims.DisplayName))
            identity.AddClaim(new Claim(ClaimTypes.Name, claims.DisplayName));
        if (!string.IsNullOrEmpty(claims.PhotoUrl))
            identity.AddClaim(new Claim("picture", claims.PhotoUrl));
        identity.AddClaim(new Claim("auth_provider", claims.Provider));

        foreach (var role in claims.Roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        foreach (var custom in claims.CustomClaims)
            identity.AddClaim(new Claim(custom.Key, custom.Value));

        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = claims.TokenExpiresAt,
                AllowRefresh = true
            });

        _logger.LogInformation("Session created for {Email} ({Uid}) via {Provider}",
            claims.Email, claims.Uid, claims.Provider);

        return Ok(new
        {
            uid = claims.Uid,
            email = claims.Email,
            displayName = claims.DisplayName,
            roles = claims.Roles
        });
    }

    /// <summary>Clear the session cookie (sign out).</summary>
    [HttpPost("signout")]
    public async Task<IActionResult> SignOut()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("Session cleared for {User}",
            User.FindFirst(ClaimTypes.Email)?.Value ?? "unknown");
        return Ok(new { signedOut = true });
    }

    /// <summary>Return current user claims from cookie (for JS interop checks).</summary>
    [HttpGet("me")]
    public IActionResult Me()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return Ok(new { authenticated = false });

        return Ok(new
        {
            authenticated = true,
            uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            email = User.FindFirst(ClaimTypes.Email)?.Value,
            displayName = User.FindFirst(ClaimTypes.Name)?.Value,
            photoUrl = User.FindFirst("picture")?.Value,
            provider = User.FindFirst("auth_provider")?.Value,
            roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList()
        });
    }
}

public record SessionRequest(string IdToken);
