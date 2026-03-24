var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var apiService = builder.AddProject<Projects.TheWatch_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.TheWatch_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService);

builder.AddAzureFunctionsProject<Projects.TheWatch_Functions>("thewatch-functions");

builder.AddProject<Projects.TheWatch_WorkerServices>("thewatch-workerservices");

builder.Build().Run();
