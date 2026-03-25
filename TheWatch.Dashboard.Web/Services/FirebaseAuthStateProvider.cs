// =============================================================================
// FirebaseAuthStateProvider — Bridges Firebase Auth with Blazor's auth system.
// =============================================================================
// In Blazor SSR + InteractiveServer, auth state flows via cookies:
//   1. Firebase JS SDK handles sign-in (popup) on the client
//   2. Client sends ID token to /api/auth/session endpoint
//   3. Server validates token via IAuthPort and sets a session cookie
//   4. This provider reads claims from HttpContext.User (populated by cookie auth)
//
// For InteractiveServer components (SignalR circuits), the initial HttpContext
// is captured at circuit start and persists for the circuit's lifetime.
// =============================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace TheWatch.Dashboard.Web.Services;

public class FirebaseAuthStateProvider : ServerAuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FirebaseAuthStateProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(new AuthenticationState(user));
    }
}
