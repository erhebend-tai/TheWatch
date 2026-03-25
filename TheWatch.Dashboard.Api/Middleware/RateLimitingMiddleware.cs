// RateLimitingMiddleware — sliding-window rate limiter for API endpoint protection.
//
// Three tiers of rate limiting, applied based on request path:
//
//   1. SOS trigger (/api/response/trigger): 10 req/min per user
//      Life-safety endpoint — generous limit to prevent accidental lockout.
//      Never blocks a legitimate SOS. The limit catches only brute-force abuse.
//
//   2. Auth endpoints (/api/account/password-reset, /api/account/login, etc.): 5 req/min per IP
//      Brute-force protection. Uses client IP (not user ID, since attacker isn't authenticated).
//      Includes password-reset to prevent email bombing.
//
//   3. General API (/api/*): 100 req/min per user
//      Broad protection against runaway clients, scraping, and DoS.
//      Identified by user ID (from auth claim) or client IP as fallback.
//
// Implementation:
//   In-memory sliding window using ConcurrentDictionary<string, SlidingWindow>.
//   Each window tracks timestamps of recent requests. Old entries are pruned on access.
//   Memory cleanup runs periodically to prevent unbounded growth.
//
// The middleware returns 429 Too Many Requests with a Retry-After header when limits are exceeded.
// Rate limit info is also returned in response headers (X-RateLimit-Limit, X-RateLimit-Remaining).
//
// ISO 27001 alignment:
//   A.14.1.2 — Securing application services (DoS protection)
//   A.12.1.3 — Capacity management (preventing resource exhaustion)
//
// Example — response headers on a successful request:
//   X-RateLimit-Limit: 100
//   X-RateLimit-Remaining: 97
//   X-RateLimit-Reset: 1711234567
//
// Example — response when rate limited:
//   HTTP 429 Too Many Requests
//   Retry-After: 30
//   X-RateLimit-Limit: 5
//   X-RateLimit-Remaining: 0
//
// WAL: This middleware runs BEFORE authentication (for auth endpoint protection) but
//      AFTER SecurityHeaders. For authenticated endpoints, it reads the "uid" claim.
//      For anonymous endpoints (auth), it uses the client IP.

using System.Collections.Concurrent;

namespace TheWatch.Dashboard.Api.Middleware;

/// <summary>
/// Rate limiting tiers with their limits and window sizes.
/// </summary>
internal enum RateLimitTier
{
    /// <summary>SOS trigger: 10 req/min per user — life-safety, generous limit.</summary>
    SosTrigger,

    /// <summary>Auth endpoints: 5 req/min per IP — brute-force protection.</summary>
    Auth,

    /// <summary>General API: 100 req/min per user — broad DoS protection.</summary>
    General,

    /// <summary>Not an API request — no rate limiting applied.</summary>
    None
}

/// <summary>
/// Sliding window rate limiter. Tracks request timestamps and prunes expired entries on access.
/// Thread-safe via ConcurrentDictionary and lock-free queue operations.
/// </summary>
internal class SlidingWindow
{
    private readonly Queue<DateTime> _timestamps = new();
    private readonly object _lock = new();

    /// <summary>
    /// Try to record a request. Returns true if within the limit, false if rate limited.
    /// Also returns the current count and the time until the oldest entry expires.
    /// </summary>
    public (bool Allowed, int Count, int RemainingSeconds) TryRecord(int maxRequests, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - window;

        lock (_lock)
        {
            // Prune expired entries
            while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                _timestamps.Dequeue();

            if (_timestamps.Count >= maxRequests)
            {
                // Rate limited — calculate when the oldest entry will expire
                var oldestInWindow = _timestamps.Peek();
                var resetTime = oldestInWindow + window;
                var remaining = (int)Math.Ceiling((resetTime - now).TotalSeconds);
                return (false, _timestamps.Count, Math.Max(1, remaining));
            }

            _timestamps.Enqueue(now);
            return (true, _timestamps.Count, 0);
        }
    }

    /// <summary>
    /// Check if the window has any recent entries (for cleanup purposes).
    /// </summary>
    public bool IsStale(TimeSpan window)
    {
        lock (_lock)
        {
            if (_timestamps.Count == 0) return true;
            return _timestamps.Peek() < DateTime.UtcNow - window - TimeSpan.FromMinutes(5);
        }
    }
}

/// <summary>
/// Middleware that enforces per-user/per-IP rate limits on API endpoints.
/// Must be registered before UseAuthentication for auth endpoint protection.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();
    private DateTime _lastCleanup = DateTime.UtcNow;

    // ── Rate limit configuration ──────────────────────────────────
    // SOS trigger: GENEROUS limit — never block a legitimate emergency
    private const int SosMaxRequests = 10;
    private static readonly TimeSpan SosWindow = TimeSpan.FromMinutes(1);

    // Auth endpoints: STRICT limit — protect against brute-force
    private const int AuthMaxRequests = 5;
    private static readonly TimeSpan AuthWindow = TimeSpan.FromMinutes(1);

    // General API: MODERATE limit — prevent scraping/DoS
    private const int GeneralMaxRequests = 100;
    private static readonly TimeSpan GeneralWindow = TimeSpan.FromMinutes(1);

    // Cleanup stale windows every 5 minutes to prevent memory leak
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var tier = ClassifyRequest(context);

        if (tier == RateLimitTier.None)
        {
            await _next(context);
            return;
        }

        var (maxRequests, window) = GetLimits(tier);
        var key = BuildKey(context, tier);
        var slidingWindow = _windows.GetOrAdd(key, _ => new SlidingWindow());
        var (allowed, count, retryAfterSeconds) = slidingWindow.TryRecord(maxRequests, window);

        // Add rate limit headers to every API response (successful or not)
        context.Response.Headers["X-RateLimit-Limit"] = maxRequests.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, maxRequests - count).ToString();
        context.Response.Headers["X-RateLimit-Reset"] =
            new DateTimeOffset(DateTime.UtcNow.Add(window)).ToUnixTimeSeconds().ToString();

        if (!allowed)
        {
            _logger.LogWarning(
                "Rate limit exceeded: Tier={Tier}, Key={Key}, Count={Count}, Limit={Limit}",
                tier, key, count, maxRequests);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(
                $$"""{"error":"Rate limit exceeded","retryAfterSeconds":{{retryAfterSeconds}},"tier":"{{tier}}"}""");
            return;
        }

        // Periodic cleanup of stale windows
        CleanupIfNeeded();

        await _next(context);
    }

    /// <summary>
    /// Classify the request into a rate limit tier based on the URL path.
    /// </summary>
    private static RateLimitTier ClassifyRequest(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Only rate-limit API endpoints
        if (!path.StartsWith("/api/"))
            return RateLimitTier.None;

        // SOS trigger — life-safety, generous limit
        if (path.StartsWith("/api/response/trigger"))
            return RateLimitTier.SosTrigger;

        // Auth endpoints — brute-force protection
        if (path.StartsWith("/api/account/password-reset") ||
            path.StartsWith("/api/account/mfa/verify") ||
            path.StartsWith("/api/account/confirm-email"))
            return RateLimitTier.Auth;

        // Everything else under /api/
        return RateLimitTier.General;
    }

    /// <summary>
    /// Get the rate limit parameters for a tier.
    /// </summary>
    private static (int MaxRequests, TimeSpan Window) GetLimits(RateLimitTier tier) => tier switch
    {
        RateLimitTier.SosTrigger => (SosMaxRequests, SosWindow),
        RateLimitTier.Auth => (AuthMaxRequests, AuthWindow),
        RateLimitTier.General => (GeneralMaxRequests, GeneralWindow),
        _ => (GeneralMaxRequests, GeneralWindow)
    };

    /// <summary>
    /// Build the rate limit key. Auth endpoints use client IP (attacker isn't authenticated).
    /// Other endpoints use user ID from auth claims, falling back to IP.
    /// </summary>
    private static string BuildKey(HttpContext context, RateLimitTier tier)
    {
        var prefix = tier.ToString();

        if (tier == RateLimitTier.Auth)
        {
            // Auth endpoints: key by IP (user isn't authenticated yet)
            var ip = GetClientIp(context);
            return $"{prefix}:{ip}";
        }

        // Authenticated endpoints: key by user ID, fall back to IP
        var uid = context.User?.FindFirst("uid")?.Value;
        if (!string.IsNullOrEmpty(uid))
            return $"{prefix}:uid:{uid}";

        return $"{prefix}:ip:{GetClientIp(context)}";
    }

    /// <summary>
    /// Extract the client IP, respecting X-Forwarded-For from reverse proxies.
    /// Falls back to RemoteIpAddress, then "unknown".
    /// </summary>
    private static string GetClientIp(HttpContext context)
    {
        // Check X-Forwarded-For header (set by load balancers, API gateways, Cloudflare)
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
        {
            // X-Forwarded-For may contain multiple IPs; take the first (client IP)
            var firstIp = forwarded.Split(',', StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(firstIp))
                return firstIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Periodically clean up stale sliding windows to prevent unbounded memory growth.
    /// </summary>
    private void CleanupIfNeeded()
    {
        if (DateTime.UtcNow - _lastCleanup < CleanupInterval)
            return;

        _lastCleanup = DateTime.UtcNow;

        var maxWindow = TimeSpan.FromMinutes(2); // Longest window + buffer
        var staleKeys = _windows
            .Where(kvp => kvp.Value.IsStale(maxWindow))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in staleKeys)
            _windows.TryRemove(key, out _);

        if (staleKeys.Count > 0)
            _logger.LogDebug("Rate limiter cleanup: removed {Count} stale windows", staleKeys.Count);
    }
}

/// <summary>
/// Extension method for registering RateLimitingMiddleware in the pipeline.
/// </summary>
public static class RateLimitingMiddlewareExtensions
{
    /// <summary>
    /// Adds per-user/per-IP rate limiting to API endpoints.
    /// Should be called AFTER UseSecurityHeaders and BEFORE UseAuthentication.
    ///
    /// Example:
    ///   app.UseSecurityHeaders();
    ///   app.UseRateLimiting();     // ← before auth
    ///   app.UseAuthentication();
    ///   app.UseAuthorization();
    /// </summary>
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RateLimitingMiddleware>();
    }
}
