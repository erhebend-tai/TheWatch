// StorageResult<T> — envelope for storage operations, carries success/error + ETag for optimistic concurrency.
// Example:
//   var result = await storage.RetrieveAsync<WorkItem>("wi-1");
//   if (result.Success) Console.WriteLine(result.Data!.Title);

namespace TheWatch.Shared.Domain.Models;

public class StorageResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ETag { get; set; }

    public static StorageResult<T> Ok(T data, string? etag = null) =>
        new() { Success = true, Data = data, ETag = etag };

    public static StorageResult<T> Fail(string error) =>
        new() { Success = false, ErrorMessage = error };
}
