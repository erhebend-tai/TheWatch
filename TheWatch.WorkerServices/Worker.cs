// This file was moved to `Workers\Worker.cs` and replaced with a simple redirecting type to
// preserve binary compatibility for any external consumers that referenced the original
// `TheWatch.WorkerServices.Worker` type. It forwards construction to the implementation in
// `TheWatch.WorkerServices.Workers.Worker`.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TheWatch.WorkerServices;

public sealed class Worker : BackgroundService
{
    private readonly TheWatch.WorkerServices.Workers.Worker _impl;

    public Worker(ILogger<TheWatch.WorkerServices.Workers.Worker> logger)
    {
        // Create the implementation instance and pass-through lifetime.
        _impl = new TheWatch.WorkerServices.Workers.Worker(logger);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Invoke the implementation's ExecuteAsync via reflection since it's protected
        var method = typeof(TheWatch.WorkerServices.Workers.Worker).GetMethod(
            "ExecuteAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            new[] { typeof(CancellationToken) },
            null);

        return (Task)method!.Invoke(_impl, new object[] { stoppingToken })!;
    }
}
