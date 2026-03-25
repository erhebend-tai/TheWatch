// SecurityHeadersMiddleware — adds defense-in-depth HTTP security headers to every response.
//
// Headers added:
//   X-Content-Type-Options: nosniff         — Prevents MIME-type sniffing (IE/Chrome)
//   X-Frame-Options: DENY                   — Clickjacking protection (frames/iframes)
//   X-XSS-Protection: 1; mode=block         — Legacy XSS filter (Chrome < 78, IE)
//   Strict-Transport-Security: max-age=...  — HSTS: force HTTPS for 1 year + subdomains
//   Content-Security-Policy: ...            — CSP: restrict script/style/connect sources
//   Referrer-Policy: strict-origin-when-cross-origin — Limit referrer leakage
//   Permissions-Policy: ...                 — Restrict browser feature access
//   X-Permitted-Cross-Domain-Policies: none — Block Flash/PDF cross-domain policy files
//   Cache-Control: no-store                 — Prevent sensitive data caching (API responses)
//
// SignalR compatibility:
//   The CSP allows 'self' and wss: for WebSocket connections (required by SignalR).
//   Blazor Server/WASM requires 'unsafe-inline' for styles and 'unsafe-eval' for scripts
//   in development only — production CSP is stricter.
//
// ISO 27001 alignment:
//   A.14.1.2 — Securing application services on public networks
//   A.14.1.3 — Protecting application service transactions
//
// Example — verifying headers:
//   curl -I https://localhost:7001/api/health
//   # Look for: X-Content-Type-Options, X-Frame-Options, Strict-Transport-Security, etc.
//
// WAL: This middleware runs EARLY in the pipeline (before routing) so headers are
//      added even on error responses, 404s, and middleware short-circuits.

namespace TheWatch.Dashboard.Api.Middleware;

/// <summary>
/// Middleware that adds security headers to all HTTP responses.
/// Should be registered early in the pipeline (before routing/auth).
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _isDevelopment;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _isDevelopment = env.IsDevelopment();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // ── Prevent MIME-type sniffing ────────────────────────────
        // Stops browsers from guessing Content-Type, preventing XSS via content-type confusion.
        // Reference: https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/X-Content-Type-Options
        headers["X-Content-Type-Options"] = "nosniff";

        // ── Clickjacking protection ──────────────────────────────
        // DENY prevents any site (including ourselves) from framing our pages.
        // Use SAMEORIGIN if embedding in our own Blazor app is needed.
        // Reference: https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/X-Frame-Options
        headers["X-Frame-Options"] = "DENY";

        // ── Legacy XSS filter ────────────────────────────────────
        // Deprecated in modern browsers but still useful for IE and older Chrome.
        // mode=block prevents the page from rendering if XSS is detected.
        // Reference: https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/X-XSS-Protection
        headers["X-XSS-Protection"] = "1; mode=block";

        // ── HSTS (HTTP Strict Transport Security) ─────────────────
        // Forces HTTPS for 1 year (31536000s), includes subdomains, allows preload.
        // Only set in production/staging — dev uses HTTP for local debugging.
        // Reference: https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Strict-Transport-Security
        if (!_isDevelopment)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
        }

        // ── Content Security Policy ──────────────────────────────
        // Restricts where scripts, styles, images, and connections can load from.
        // SignalR needs 'self' + wss: for WebSocket connections.
        // Blazor needs 'unsafe-inline' for component styles and 'wasm-unsafe-eval' for WASM.
        //
        // Development CSP is more permissive to allow hot-reload, Swagger UI, etc.
        // Production CSP is strict — only self-hosted resources and known CDNs.
        if (_isDevelopment)
        {
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: https:; " +
                "font-src 'self' data:; " +
                "connect-src 'self' ws: wss: http: https:; " +
                "frame-ancestors 'none';";
        }
        else
        {
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'wasm-unsafe-eval'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: https:; " +
                "font-src 'self'; " +
                "connect-src 'self' wss: https:; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'; " +
                "upgrade-insecure-requests;";
        }

        // ── Referrer Policy ──────────────────────────────────────
        // Sends full URL as referrer for same-origin requests, only origin for cross-origin.
        // Prevents leaking sensitive URL paths (e.g., /api/gdpr/export/{userId}) to third parties.
        // Reference: https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Referrer-Policy
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // ── Permissions Policy (formerly Feature-Policy) ──────────
        // Restricts which browser features the page can use.
        // We allow geolocation (for the web dashboard map), camera and microphone
        // (for Watch Calls), but deny everything else.
        // Reference: https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Permissions-Policy
        headers["Permissions-Policy"] =
            "geolocation=(self), " +
            "camera=(self), " +
            "microphone=(self), " +
            "payment=(), " +
            "usb=(), " +
            "magnetometer=(), " +
            "gyroscope=(), " +
            "accelerometer=()";

        // ── Cross-Domain Policy ──────────────────────────────────
        // Prevents Adobe Flash and PDF readers from reading data from this domain.
        // Reference: https://owasp.org/www-project-secure-headers/
        headers["X-Permitted-Cross-Domain-Policies"] = "none";

        // ── Cache Control for API responses ──────────────────────
        // API responses contain sensitive data (locations, PII, evidence metadata).
        // no-store prevents caching by browsers, proxies, and CDNs.
        // Static files are served by Blazor's StaticFileMiddleware with proper caching headers.
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            headers["Pragma"] = "no-cache";
        }

        await _next(context);
    }
}

/// <summary>
/// Extension method for registering SecurityHeadersMiddleware in the pipeline.
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>
    /// Adds security headers to all HTTP responses.
    /// Should be called EARLY in the middleware pipeline (before UseRouting, UseAuth).
    ///
    /// Example:
    ///   app.UseSecurityHeaders();   // ← early in pipeline
    ///   app.UseRouting();
    ///   app.UseAuthentication();
    ///   app.UseAuthorization();
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
