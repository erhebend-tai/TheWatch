using SQLite;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TheWatch.Shared.Models.Sync;

namespace TheWatch.Maui.Services;

public class SyncTaskStore
{
    private const string DbName = "TheWatchSync.db3";
    private readonly SQLiteAsyncConnection _database;

    public SyncTaskStore()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, DbName);
        _database = new SQLiteAsyncConnection(dbPath);
        _database.CreateTableAsync<SyncTask>().Wait();
    }

    public Task<int> AddTaskAsync(SyncTask task)
    {
        task.CreatedAt = DateTime.UtcNow;
        task.Status = SyncTaskStatus.Pending;
        return _database.InsertAsync(task);
    }

    public async Task<SyncTask?> GetNextPendingTaskAsync()
    {
        return await _database.Table<SyncTask>()
                                .Where(t => t.Status == SyncTaskStatus.Pending)
                                .OrderBy(t => t.CreatedAt)
                                .FirstOrDefaultAsync();
    }

    public async Task UpdateTaskStatusAsync(int taskId, SyncTaskStatus status, bool incrementAttempt = false)
    {
        var task = await _database.FindAsync<SyncTask>(taskId);
        if (task != null)
        {
            task.Status = status;
            task.UpdatedAt = DateTime.UtcNow;
            if (incrementAttempt)
            {
                task.Attempts++;
            }
            await _database.UpdateAsync(task);
        }
    }

    public Task<int> PruneCompletedTasksAsync(System.TimeSpan retentionPeriod)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;
        return _database.Table<SyncTask>()
                        .Where(t => t.Status == SyncTaskStatus.Completed && t.UpdatedAt < cutoff)
                        .DeleteAsync();
    }
}
