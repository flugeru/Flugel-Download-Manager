namespace Harbor.Core;

public enum DownloadState { Queued, Scheduled, Downloading, Paused, Completed, Failed }

public sealed record DownloadSnapshot(string Id, string Url, string FileName, string Destination,
    DownloadState State, long Received, long? Total, double BytesPerSecond, string? Error,
    DateTimeOffset? ScheduledAt, int Segments)
{
    public double Percent => Total is > 0 ? Math.Clamp(Received * 100d / Total.Value, 0, 100) : 0;
    public string ProgressText => Total is { } total ? $"{Size(Received)} / {Size(total)}" : Size(Received);
    public string SpeedText => BytesPerSecond > 0 ? $"{Size(BytesPerSecond)}/s" : "—";
    public string EtaText => Total is { } total && BytesPerSecond > 0
        ? TimeSpan.FromSeconds(Math.Min(TimeSpan.MaxValue.TotalSeconds - 1, Math.Max(0, total - Received) / BytesPerSecond)).ToString(@"d\.hh\:mm\:ss") : "—";
    private static string Size(double n) => n >= 1073741824 ? $"{n / 1073741824:0.0} GiB" : n >= 1048576 ? $"{n / 1048576:0.0} MiB" : n >= 1024 ? $"{n / 1024:0.0} KiB" : $"{n:0} B";
}

public sealed class ManagerSettings
{
    public string DownloadDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public int MaxConcurrent { get; set; } = 2;
    public int Segments { get; set; } = 4;
    public int MaxRetries { get; set; } = 3;
}
