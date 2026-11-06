namespace Flugel.Core;

public enum DownloadState { Queued, Scheduled, Downloading, Paused, Completed, Failed }

public sealed record SegmentSnapshot(int Index, long Start, long Length, long Received, string StateText)
{
    public double Percent => Length is > 0 and < long.MaxValue ? Math.Clamp(Received * 100d / Length, 0, 100) : 0;
    public string RangeText => Length is > 0 and < long.MaxValue
        ? $"{DownloadSnapshot.FormatSize(Received)} / {DownloadSnapshot.FormatSize(Length)}"
        : DownloadSnapshot.FormatSize(Received);
}

public sealed record DownloadSnapshot(string Id, string Url, string FileName, string Destination,
    DownloadState State, long Received, long? Total, double BytesPerSecond, string? Error,
    DateTimeOffset? ScheduledAt, int Segments)
{
    public string Category { get; init; } = "General";
    public string QueueName { get; init; } = "Main";
    public string Description { get; init; } = "";
    public string? Referrer { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastAttemptAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long? MaxBytesPerSecond { get; init; }
    public bool ResumeSupported { get; init; }
    public bool IsMediaStream { get; init; }
    public string? MediaQualityLabel { get; init; }
    public IReadOnlyList<SegmentSnapshot> SegmentDetails { get; init; } = [];

    public double Percent => Total is > 0 ? Math.Clamp(Received * 100d / Total.Value, 0, 100) : 0;
    public string SizeText => Total is { } total ? FormatSize(total) : Received > 0 ? FormatSize(Received) : "Unknown";
    public string ProgressText => Total is { } total ? $"{FormatSize(Received)} / {FormatSize(total)} ({Percent:0}%)" : FormatSize(Received);
    public string SpeedText => BytesPerSecond > 0 ? $"{FormatSize(BytesPerSecond)}/s" : "—";
    public string EtaText => Total is { } total && BytesPerSecond > 0
        ? TimeSpan.FromSeconds(Math.Min(TimeSpan.MaxValue.TotalSeconds - 1, Math.Max(0, total - Received) / BytesPerSecond)).ToString(@"hh\:mm\:ss") : "—";
    public string LastAttemptText => LastAttemptAt is { } when ? when.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—";

    public static string FormatSize(double n) =>
        n >= 1073741824 ? $"{n / 1073741824:0.00} GiB" :
        n >= 1048576 ? $"{n / 1048576:0.0} MiB" :
        n >= 1024 ? $"{n / 1024:0.0} KiB" : $"{n:0} B";
}

public sealed class CategoryRule
{
    public string Name { get; set; } = "General";
    public string Extensions { get; set; } = "";
    public string Subfolder { get; set; } = "";

    public static List<CategoryRule> CreateDefaults() =>
    [
        new() { Name = "Compressed", Extensions = "zip rar 7z gz bz2 xz tar tgz zst cab iso", Subfolder = "Compressed" },
        new() { Name = "Documents", Extensions = "pdf doc docx xls xlsx ppt pptx txt rtf csv md epub", Subfolder = "Documents" },
        new() { Name = "Music", Extensions = "mp3 wav flac aac ogg m4a wma opus", Subfolder = "Music" },
        new() { Name = "Programs", Extensions = "exe msi msix appx bat cmd ps1 jar dmg deb rpm", Subfolder = "Programs" },
        new() { Name = "Video", Extensions = "mp4 mkv webm avi mov wmv flv m4v ts m3u8 mpd mpg mpeg", Subfolder = "Video" },
        new() { Name = "General", Extensions = "", Subfolder = "" }
    ];
}

public sealed class QueueDefinition
{
    public string Name { get; set; } = "Main";
    public bool IsStarted { get; set; } = true;
    public int MaxConcurrent { get; set; } = 4;
    public bool EnableScheduleStart { get; set; }
    public string DailyStartTime { get; set; } = "00:00";
    public bool EnableScheduleStop { get; set; }
    public string DailyStopTime { get; set; } = "23:59";
    public List<DayOfWeek> ActiveDays { get; set; } =
    [
        DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
    ];

    public static List<QueueDefinition> CreateDefaults() =>
    [
        new() { Name = "Main", IsStarted = true, MaxConcurrent = 4 },
        new() { Name = "Synchronization", IsStarted = false, MaxConcurrent = 1 }
    ];
}

public sealed class AddDownloadOptions
{
    public string Url { get; set; } = "";
    public string DestinationDirectory { get; set; } = "";
    public string? FileName { get; set; }
    public string? Category { get; set; }
    public string QueueName { get; set; } = "Main";
    public string Description { get; set; } = "";
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public string? BasicAuthUser { get; set; }
    public string? BasicAuthPassword { get; set; }
    // Browser session cookies for this exact URL, sent only when the user explicitly enables it for the site.
    // Held in memory for the transfer; never persisted or logged.
    public string? CookieHeader { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public int? Segments { get; set; }
    public long? MaxBytesPerSecond { get; set; }
    public bool StartImmediately { get; set; } = true;
    public bool UseCategorySubfolder { get; set; }
    public bool ResolveServerFileName { get; set; }
    public string? MediaQualityLabel { get; set; }
    public List<string>? MediaSegmentUrls { get; set; }
    // Correlation id from the browser extension so the handoff can be reconciled after crashes.
    public string? RequestId { get; set; }
}

public sealed record UrlProbeResult(
    bool Success,
    string EffectiveUrl,
    string SuggestedFileName,
    string SuggestedCategory,
    long? ContentLength,
    string? ContentType,
    bool AcceptsRanges,
    bool IsHlsManifest,
    bool IsDashManifest,
    string? ErrorMessage);

public sealed class ManagerSettings
{
    public string DownloadDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public int MaxConcurrent { get; set; } = 2;
    public int Segments { get; set; } = 4;
    public int MaxRetries { get; set; } = 3;

    // Extended IDM-equivalent capabilities
    public bool UseCategorySubfolders { get; set; }
    public bool SpeedLimiterEnabled { get; set; }
    public long MaxGlobalBytesPerSecond { get; set; } = 512 * 1024;
    public bool ShowCompletionDialog { get; set; } = true;
    public bool StartQueueImmediately { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool MonitorClipboard { get; set; }
    public bool BrowserAutoIntercept { get; set; } = true;
    public bool BrowserVideoBalloonEnabled { get; set; } = true;
    public string AutoCaptureExtensions { get; set; } = "7Z AAC ACE AIF ARJ ASF AVI BIN BZ2 EXE GZ GZIP IMG ISO LZH M4A M4V MKV MOV MP3 MP4 MPA MPE MPEG MPG MSI MSU OGG OGV PDF PLJ PPS PPT QT R0* R1* RA RAR RM RMVB SEA SIT SITX TAR TIF TIFF WAV WMA WMV Z ZIP WEBM FLAC OPUS";
    public string ExcludedHosts { get; set; } = "";
    public string ProxyMode { get; set; } = "System"; // None, System, Manual
    public string? ProxyAddress { get; set; }
    public string? ProxyUsername { get; set; }
    public List<CategoryRule> Categories { get; set; } = CategoryRule.CreateDefaults();
    public List<QueueDefinition> Queues { get; set; } = QueueDefinition.CreateDefaults();
}
