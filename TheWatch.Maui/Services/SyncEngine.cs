using System.Threading;
using System.Threading.Tasks;
using TheWatch.Shared.Models.Sync;

namespace TheWatch.Maui.Services;

public class SyncEngine : ISyncEngine
{
    private readonly SyncTaskStore _syncTaskStore;
    private CancellationTokenSource _cts;

    public SyncEngine(SyncTaskStore syncTaskStore)
    {
        _syncTaskStore = syncTaskStore;
        _cts = new CancellationTokenSource();
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(async () => await SyncLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts.Cancel();
    }

    private async Task SyncLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var task = await _syncTaskStore.GetNextPendingTaskAsync();
            if (task != null)
            {
                await ProcessTask(task);
            }
            else
            {
                // No pending tasks, wait a bit before checking again.
                await Task.Delay(5000, token); 
            }
        }
    }

    private async Task ProcessTask(SyncTask task)
    {
        await _syncTaskStore.UpdateTaskStatusAsync(task.Id, SyncTaskStatus.InProgress, incrementAttempt: true);

        bool success = await UploadData(task);

        if (success)
        {
            await _syncTaskStore.UpdateTaskStatusAsync(task.Id, SyncTaskStatus.Completed);
        }
        else
        {
            // Simplified retry/dead-letter logic.
            // A real app might have more sophisticated backoff strategies.
            if (task.Attempts >= 5) 
            {
                await _syncTaskStore.UpdateTaskStatusAsync(task.Id, SyncTaskStatus.DeadLetter);
            }
            else
            {
                await _syncTaskStore.UpdateTaskStatusAsync(task.Id, SyncTaskStatus.Pending);
            }
        }
    }

    private async Task<bool> UploadData(SyncTask task)
    {
        // This is where you'd use HttpClient to send the data to your server.
        // For this example, we'll just simulate a network call.
        System.Diagnostics.Debug.WriteLine($"[SyncEngine] Uploading task {task.Id}: {task.Payload}");

        await Task.Delay(1000); // Simulate network latency

        // Simulate transient failures.
        // In a real scenario, this would be based on the HTTP response.
        bool success = new System.Random().Next(0, 4) != 0; // 75% success rate

        System.Diagnostics.Debug.WriteLine($"[SyncEngine] Upload result for task {task.Id}: {(success ? "Success" : "Failure")}");

        return success;
    }
}
