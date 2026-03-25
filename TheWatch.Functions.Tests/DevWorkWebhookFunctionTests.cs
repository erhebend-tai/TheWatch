// DevWorkWebhookFunctionTests - xUnit tests for DevWorkWebhookFunction.
// The function has HTTP triggers (HttpRequestData) which are difficult to mock
// in the isolated worker model without a full test server. We verify construction
// and that the function class can be instantiated with NullLogger.
//
// WAL: DevWorkWebhookFunction has three HTTP endpoints:
//   POST /api/devwork/github    — GitHub webhook (reads X-GitHub-Event, X-GitHub-Delivery headers)
//   POST /api/devwork/firestore — Firestore webhook (parses document/operation from JSON body)
//   POST /api/devwork/custom    — Custom webhook (reads X-Webhook-Source, X-Webhook-Event headers)
//
// Since HttpRequestData is abstract and requires FunctionContext to construct,
// these tests verify:
//   1. Function instantiation with DI logger
//   2. The function class exposes the expected public methods
//   3. Integration-level HTTP tests would go in a separate integration test project
//
// Example (integration test, not unit):
//   var host = new HostBuilder().ConfigureFunctionsWorkerDefaults().Build();
//   var client = host.GetTestClient();
//   var response = await client.PostAsync("/api/devwork/github", content);

namespace TheWatch.Functions.Tests;

public class DevWorkWebhookFunctionTests
{
    private readonly DevWorkWebhookFunction _sut;
    private readonly ILogger<DevWorkWebhookFunction> _logger;

    public DevWorkWebhookFunctionTests()
    {
        _logger = NullLoggerFactory.Instance.CreateLogger<DevWorkWebhookFunction>();
        _sut = new DevWorkWebhookFunction(_logger);
    }

    [Fact]
    public void Constructor_WithLogger_CreatesInstance()
    {
        // Assert — the function was successfully instantiated
        Assert.NotNull(_sut);
    }

    [Fact]
    public void GitHubWebhook_MethodExists_WithCorrectSignature()
    {
        // Verify the method exists with the expected return type
        var method = typeof(DevWorkWebhookFunction).GetMethod("GitHubWebhook");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Microsoft.Azure.Functions.Worker.Http.HttpResponseData>), method!.ReturnType);
    }

    [Fact]
    public void FirestoreWebhook_MethodExists_WithCorrectSignature()
    {
        var method = typeof(DevWorkWebhookFunction).GetMethod("FirestoreWebhook");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Microsoft.Azure.Functions.Worker.Http.HttpResponseData>), method!.ReturnType);
    }

    [Fact]
    public void CustomWebhook_MethodExists_WithCorrectSignature()
    {
        var method = typeof(DevWorkWebhookFunction).GetMethod("CustomWebhook");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Microsoft.Azure.Functions.Worker.Http.HttpResponseData>), method!.ReturnType);
    }

    [Fact]
    public void AllEndpoints_HaveFunctionAttribute()
    {
        // Verify all three methods are decorated with [Function(...)]
        var methods = new[] { "GitHubWebhook", "FirestoreWebhook", "CustomWebhook" };
        foreach (var name in methods)
        {
            var method = typeof(DevWorkWebhookFunction).GetMethod(name);
            Assert.NotNull(method);
            var attr = method!.GetCustomAttributes(typeof(Microsoft.Azure.Functions.Worker.FunctionAttribute), false);
            Assert.Single(attr);
        }
    }

    [Fact]
    public void FunctionNames_AreCorrect()
    {
        // Verify the [Function("...")] name attribute values
        var expected = new Dictionary<string, string>
        {
            ["GitHubWebhook"] = "DevWorkGitHubWebhook",
            ["FirestoreWebhook"] = "DevWorkFirestoreWebhook",
            ["CustomWebhook"] = "DevWorkCustomWebhook"
        };

        foreach (var (methodName, functionName) in expected)
        {
            var method = typeof(DevWorkWebhookFunction).GetMethod(methodName);
            var attr = (Microsoft.Azure.Functions.Worker.FunctionAttribute)
                method!.GetCustomAttributes(typeof(Microsoft.Azure.Functions.Worker.FunctionAttribute), false).First();
            Assert.Equal(functionName, attr.Name);
        }
    }
}
