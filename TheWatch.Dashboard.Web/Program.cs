// =============================================================================
// TheWatch.Dashboard.Web — Blazor SSR Frontend
// =============================================================================
// Connects to Dashboard.Api via Aspire service discovery ("https+http://dashboard-api").
// Uses Redis for output caching and SignalR for real-time updates.
//
// Example — service discovery resolves the API URL automatically:
//   var client = new HttpClient { BaseAddress = new Uri("https+http://dashboard-api") };
//   // Aspire resolves this to the actual API endpoint at runtime.
//
// WAL: Blazor SSR renders on the server. Interactive components use SignalR.
// =============================================================================

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor.Services;
using Serilog;
using TheWatch.Dashboard.Web.Components;
using TheWatch.Dashboard.Web.Services;
using TheWatch.Data.Configuration;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire Service Defaults (includes Serilog) ───────────────
builder.AddServiceDefaults();

// ── Aspire Redis (output caching + distributed cache) ────────
builder.AddRedisOutputCache("thewatch-redis");

// ── MudBlazor ────────────────────────────────────────────────
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.PreventDuplicates = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
});

// ── Authentication (Firebase Auth → cookie session) ──────────
// Cookie auth stores the validated Firebase claims server-side.
// In development, MockAuthAdapter accepts any token.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/signin";
        options.LogoutPath = "/auth/signout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, FirebaseAuthStateProvider>();
builder.Services.AddHttpContextAccessor();

// ── Auth Port (Firebase in production, Mock in development) ──
builder.Services.AddAuthPort(builder.Configuration);

// ── Controllers (for AuthController session endpoint) ────────
builder.Services.AddControllers();

// ── Swarm Inventory (Firestore-backed with static fallback) ──
builder.Services.AddScoped<SwarmInventoryService>();

// ── Blazor SSR ───────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── API Client (resolved via Aspire service discovery) ───────
builder.Services.AddHttpClient<DashboardApiClient>(
    client => client.BaseAddress = new Uri("https+http://dashboard-api"));

// ── SignalR Client ───────────────────────────────────────────
// DashboardHubService — maintains a persistent SignalR connection
// to the Dashboard.Api hub for real-time incident updates.
// Scoped: one connection per Blazor circuit (per browser tab).
builder.Services.AddScoped<DashboardHubService>();
builder.Services.AddSignalR();

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────
app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseSerilogRequestLogging(options =>
{
    // Don't log static file requests or health checks — too noisy
    options.GetLevel = (httpContext, elapsed, ex) =>
    {
        if (httpContext.Request.Path.StartsWithSegments("/health") ||
            httpContext.Request.Path.StartsWithSegments("/alive") ||
            httpContext.Request.Path.StartsWithSegments("/_framework") ||
            httpContext.Request.Path.StartsWithSegments("/_content") ||
            httpContext.Request.Path.StartsWithSegments("/css"))
            return Serilog.Events.LogEventLevel.Verbose;

        return ex is not null ? Serilog.Events.LogEventLevel.Error
            : elapsed > 1000 ? Serilog.Events.LogEventLevel.Warning
            : Serilog.Events.LogEventLevel.Information;
    };
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseOutputCache();

// Auth controller (session create/destroy)
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
