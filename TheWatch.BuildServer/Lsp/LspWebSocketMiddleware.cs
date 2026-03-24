// =============================================================================
// LspWebSocketMiddleware — WebSocket transport for LSP JSON-RPC
// =============================================================================
// Upgrades HTTP connections at /lsp to WebSocket, then attaches StreamJsonRpc
// for bidirectional LSP communication. This allows the CLI dashboard and
// agents to connect via WebSocket rather than requiring stdin/stdout piping.
//
// Example — connecting from the CLI dashboard:
//   var ws = new ClientWebSocket();
//   await ws.ConnectAsync(new Uri("ws://localhost:5002/lsp"), ct);
//   var rpc = new JsonRpc(new WebSocketMessageHandler(ws));
//   rpc.StartListening();
//   var result = await rpc.InvokeAsync<InitializeResult>("initialize");
//
// WAL: Each WebSocket connection gets its own JsonRpc instance sharing the
//      same LspServer singleton. Multiple clients can connect simultaneously.
// =============================================================================

using System.Net.WebSockets;
using StreamJsonRpc;

namespace TheWatch.BuildServer.Lsp;

public class LspWebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LspWebSocketMiddleware> _logger;

    public LspWebSocketMiddleware(RequestDelegate next, IServiceProvider serviceProvider, ILogger<LspWebSocketMiddleware> logger)
    {
        _next = next;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/lsp" && context.WebSockets.IsWebSocketRequest)
        {
            var ws = await context.WebSockets.AcceptWebSocketAsync();
            _logger.LogInformation("LSP WebSocket client connected from {Remote}", context.Connection.RemoteIpAddress);

            var lspServer = _serviceProvider.GetRequiredService<LspServer>();
            var handler = new WebSocketMessageHandler(ws);
            var rpc = new JsonRpc(handler, lspServer);

            rpc.Disconnected += (_, e) =>
                _logger.LogInformation("LSP WebSocket client disconnected: {Reason}", e.Description);

            rpc.StartListening();

            // Keep the connection alive until the client disconnects
            await rpc.Completion;

            _logger.LogInformation("LSP WebSocket session ended");
        }
        else
        {
            await _next(context);
        }
    }
}

public static class LspWebSocketMiddlewareExtensions
{
    public static IApplicationBuilder UseLspWebSocket(this IApplicationBuilder app)
    {
        return app.UseMiddleware<LspWebSocketMiddleware>();
    }
}
