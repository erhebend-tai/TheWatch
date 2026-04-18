using SQLite;
using System;

namespace TheWatch.Shared.Models.Sync;

[Table("SyncTasks")]
public class SyncTask
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public SyncTaskStatus Status { get; set; }

    public string DataType { get; set; } 

    public string Payload { get; set; } 

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int Attempts { get; set; }
}
