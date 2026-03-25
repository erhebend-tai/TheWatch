using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TheWatch.Dashboard.Api.Auth;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using Xunit;

namespace TheWatch.Dashboard.Api.Tests;

public class FirebaseAuthenticationHandlerTests
{
    private static async Task<AuthenticateResult> RunHandler(
        IAuthPort authPort, HttpContext httpContext)
    {
        var options = new AuthenticationSchemeOptions();
        var optionsMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Get(FirebaseAuthenticationHandler.SchemeName).Returns(options);

        var loggerFactory = NullLoggerFactory.Instance;
        var encoder = UrlEncoder.Default;

        var handler = new FirebaseAuthenticationHandler(
            authPort, optionsMonitor, loggerFactory, encoder);

        var scheme = new AuthenticationScheme(
            FirebaseAuthenticationHandler.SchemeName,
            null,
            typeof(FirebaseAuthenticationHandler));

        await handler.InitializeAsync(scheme, httpContext);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task ValidBearerToken_ReturnsSuccess_WithCorrectClaims()
    {
        // Arrange
        var authPort = Substitute.For<IAuthPort>();
        authPort.ValidateTokenAsync("valid-token", Arg.Any<CancellationToken>())
            .Returns(StorageResult<WatchUserClaims>.Ok(new WatchUserClaims
            {
                Uid = "user-123",
                Email = "test@thewatch.app",
                DisplayName = "Test User",
                Provider = "firebase",
                Roles = new List<string> { "operator" },
                TokenIssuedAt = DateTime.UtcNow.AddMinutes(-5),
                TokenExpiresAt = DateTime.UtcNow.AddHours(1)
            }));

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer valid-token";

        // Act
        var result = await RunHandler(authPort, context);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Principal);
        Assert.Equal("user-123", result.Principal!.FindFirst("uid")?.Value);
        Assert.Equal("test@thewatch.app", result.Principal.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal("Test User", result.Principal.FindFirst(ClaimTypes.Name)?.Value);
        Assert.True(result.Principal.IsInRole("operator"));
    }

    [Fact]
    public async Task MissingAuthorizationHeader_ReturnsNoResult()
    {
        var authPort = Substitute.For<IAuthPort>();
        var context = new DefaultHttpContext();

        var result = await RunHandler(authPort, context);

        Assert.False(result.Succeeded);
        Assert.True(result.None);
    }

    [Fact]
    public async Task InvalidToken_ReturnsFail()
    {
        var authPort = Substitute.For<IAuthPort>();
        authPort.ValidateTokenAsync("bad-token", Arg.Any<CancellationToken>())
            .Returns(StorageResult<WatchUserClaims>.Fail("Token expired"));

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer bad-token";

        var result = await RunHandler(authPort, context);

        Assert.False(result.Succeeded);
        Assert.False(result.None);
        Assert.Contains("Token expired", result.Failure?.Message);
    }

    [Fact]
    public async Task SignalRQueryStringToken_AuthenticatesForHubPath()
    {
        var authPort = Substitute.For<IAuthPort>();
        authPort.ValidateTokenAsync("signalr-token", Arg.Any<CancellationToken>())
            .Returns(StorageResult<WatchUserClaims>.Ok(new WatchUserClaims
            {
                Uid = "hub-user",
                Email = "hub@thewatch.app",
                Provider = "firebase",
                TokenIssuedAt = DateTime.UtcNow.AddMinutes(-1),
                TokenExpiresAt = DateTime.UtcNow.AddHours(1)
            }));

        var context = new DefaultHttpContext();
        context.Request.Path = "/hubs/dashboard";
        context.Request.QueryString = new QueryString("?access_token=signalr-token");

        var result = await RunHandler(authPort, context);

        Assert.True(result.Succeeded);
        Assert.Equal("hub-user", result.Principal!.FindFirst("uid")?.Value);
    }

    [Fact]
    public async Task WatchUserClaims_StoredInHttpContextItems()
    {
        var authPort = Substitute.For<IAuthPort>();
        authPort.ValidateTokenAsync("ctx-token", Arg.Any<CancellationToken>())
            .Returns(StorageResult<WatchUserClaims>.Ok(new WatchUserClaims
            {
                Uid = "ctx-user",
                Email = "ctx@thewatch.app",
                Provider = "mock",
                TokenIssuedAt = DateTime.UtcNow,
                TokenExpiresAt = DateTime.UtcNow.AddHours(1)
            }));

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer ctx-token";

        await RunHandler(authPort, context);

        var storedClaims = context.Items["WatchUserClaims"] as WatchUserClaims;
        Assert.NotNull(storedClaims);
        Assert.Equal("ctx-user", storedClaims!.Uid);
    }
}
