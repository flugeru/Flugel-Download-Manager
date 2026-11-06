using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Flugel.Core;

public sealed class DownloadManager : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly string directory;
    private readonly FileStream instanceLock;
    private readonly HttpClient client;
    private readonly CancellationTokenSource shutdown = new();
    private readonly List<Job> jobs = [];
    private readonly Task scheduler;
    private ManagerSettings activeSettings;
    private bool disposed;
    private Exception? persistenceError;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public ManagerSettings Settings { get; }

    public DownloadManager(string dataDirectory)
    {
        directory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(directory);
        instanceLock = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var path = Path.Combine(directory, "state.json");
            var state = File.Exists(path) ? JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("State is empty.") : new SavedState();
            if (state.Version != 1 || state.Settings is null || state.Jobs is null) throw new InvalidDataException("Unsupported state.");
            Settings = state.Settings;
            EnsureDefaultCollections(Settings);
            activeSettings = ValidateSettings(Settings);
            var ids = new HashSet<string>();
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var job in state.Jobs)
            {
                ValidateUrl(job.Url);
                ValidateName(job.FileName);
                if (!Guid.TryParseExact(job.Id, "N", out _) || !ids.Add(job.Id) || job.Segments is < 1 or > 16 ||
                    !Enum.IsDefined(job.State) || job.Total < 0 || job.Parts is null || job.Parts.Count > 16 ||
                    job.Parts.Any(p => p.Start < 0 || p.Length < 0 || p.Received < 0 || p.Received > p.Length) || !ValidPartMetadata(job))
                    throw new InvalidDataException("Invalid job state.");
                job.DestinationDirectory = Path.GetFullPath(job.DestinationDirectory);
                if (string.IsNullOrWhiteSpace(job.Category)) job.Category = ClassifyFileCategory(job.FileName, activeSettings.Categories);
                if (string.IsNullOrWhiteSpace(job.QueueName)) job.QueueName = "Main";
                if (!destinations.Add(Destination(job))) throw new InvalidDataException("Duplicate destination.");
                if (job.State == DownloadState.Downloading) job.State = DownloadState.Paused;
                jobs.Add(job);
            }
            client = CreateHttpClient(activeSettings);
            scheduler = Task.Run(ScheduleAsync);
        }
        catch { instanceLock.Dispose(); throw; }
    }

    private static HttpClient CreateHttpClient(ManagerSettings settings)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = false,
            MaxAutomaticRedirections = 8,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            MaxConnectionsPerServer = 64
        };
        if (string.Equals(settings.ProxyMode, "None", StringComparison.OrdinalIgnoreCase))
        {
            handler.UseProxy = false;
        }
        else if (string.Equals(settings.ProxyMode, "Manual", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(settings.ProxyAddress) &&
                 Uri.TryCreate(settings.ProxyAddress, UriKind.Absolute, out var proxyUri))
        {
            var webProxy = new WebProxy(proxyUri);
            if (!string.IsNullOrWhiteSpace(settings.ProxyUsername))
                webProxy.Credentials = new NetworkCredential(settings.ProxyUsername, "");
            handler.Proxy = webProxy;
            handler.UseProxy = true;
        }
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public IReadOnlyList<DownloadSnapshot> GetSnapshots()
    {
        lock (gate)
        {
            return jobs.Where(j => !j.Removed).Select(j =>
            {
                var segs = j.Parts.Select((p, idx) => new SegmentSnapshot(
                    idx + 1,
                    p.Start,
                    p.Length,
                    p.Received,
                    j.State == DownloadState.Completed || (p.Length < long.MaxValue && p.Received >= p.Length && p.Length > 0)
                        ? "Completed"
                        : j.State == DownloadState.Downloading ? "Receiving" : j.State.ToString())).ToArray();

                return new DownloadSnapshot(
                    j.Id, j.Url, j.FileName, Destination(j), j.State,
                    j.Parts.Sum(p => p.Received), j.Total,
                    j.State == DownloadState.Downloading ? j.Speed : 0,
                    j.Error, j.ScheduledAt, j.Segments)
                {
                    Category = j.Category,
                    QueueName = j.QueueName,
                    Description = j.Description,
                    Referrer = j.Referrer,
                    CreatedAt = j.CreatedAt,
                    LastAttemptAt = j.LastAttemptAt,
                    CompletedAt = j.CompletedAt,
                    MaxBytesPerSecond = j.MaxBytesPerSecond,
                    ResumeSupported = j.Validator is not null && j.Total is > 0,
                    IsMediaStream = j.MediaSegmentUrls is { Count: > 0 } || j.Url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase),
                    MediaQualityLabel = j.MediaQualityLabel,
                    SegmentDetails = segs
                };
            }).ToArray();
        }
    }

    public string Add(string url, string destinationDirectory, string? fileName = null, DateTimeOffset? scheduledAt = null, int? segments = null) =>
        AddWithOptions(new AddDownloadOptions
        {
            Url = url,
            DestinationDirectory = destinationDirectory,
            FileName = fileName,
            ScheduledAt = scheduledAt,
            Segments = segments,
            StartImmediately = true,
            UseCategorySubfolder = false,
            ResolveServerFileName = string.IsNullOrWhiteSpace(fileName)
        });

    public string AddWithOptions(AddDownloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var uri = ValidateUrl(options.Url);
        string? fileName = options.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = Uri.UnescapeDataString(uri.AbsolutePath.Split('/').LastOrDefault() ?? "");
            if (fileName.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) && options.MediaSegmentUrls is { Count: > 0 })
                fileName = Path.ChangeExtension(fileName, ".ts");
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "download.bin";
        }
        ValidateName(fileName);

        if (!string.IsNullOrWhiteSpace(options.Referrer))
            ValidateUrl(options.Referrer);

        lock (gate)
        {
            EnsureWritable();
            int count = options.Segments ?? activeSettings.Segments;
            if (count is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(options.Segments));

            string category = !string.IsNullOrWhiteSpace(options.Category)
                ? options.Category.Trim()
                : ClassifyFileCategory(fileName, activeSettings.Categories);

            string baseDestination = Path.GetFullPath(
                string.IsNullOrWhiteSpace(options.DestinationDirectory) ? activeSettings.DownloadDirectory : options.DestinationDirectory);

            if (options.UseCategorySubfolder)
            {
                var rule = activeSettings.Categories.FirstOrDefault(c => string.Equals(c.Name, category, StringComparison.OrdinalIgnoreCase));
                if (rule is not null && !string.IsNullOrWhiteSpace(rule.Subfolder))
                    baseDestination = Path.GetFullPath(Path.Combine(baseDestination, rule.Subfolder));
            }

            string queueName = string.IsNullOrWhiteSpace(options.QueueName) ? "Main" : options.QueueName.Trim();
            if (!activeSettings.Queues.Any(q => string.Equals(q.Name, queueName, StringComparison.OrdinalIgnoreCase)))
                activeSettings.Queues.Add(new QueueDefinition { Name = queueName, IsStarted = true, MaxConcurrent = 4 });

            string target = Path.Combine(baseDestination, fileName);
            if (File.Exists(target) || Directory.Exists(target) || jobs.Any(j => !j.Removed && string.Equals(Destination(j), target, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Destination already exists or is reserved by another download.");

            Directory.CreateDirectory(baseDestination);
            DownloadState initialState = !options.StartImmediately
                ? DownloadState.Paused
                : options.ScheduledAt > DateTimeOffset.UtcNow
                    ? DownloadState.Scheduled
                    : DownloadState.Queued;

            string? authHeader = null;
            if (!string.IsNullOrWhiteSpace(options.BasicAuthUser))
            {
                string rawCreds = $"{options.BasicAuthUser}:{options.BasicAuthPassword ?? ""}";
                authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawCreds));
            }

            string? cookieHeader = ValidateCookieHeader(options.CookieHeader);

            var job = new Job
            {
                CookieHeader = cookieHeader,
                Id = Guid.NewGuid().ToString("N"),
                Url = uri.AbsoluteUri,
                DestinationDirectory = baseDestination,
                FileName = fileName,
                Category = category,
                QueueName = queueName,
                Description = options.Description?.Trim() ?? "",
                Referrer = string.IsNullOrWhiteSpace(options.Referrer) ? null : options.Referrer.Trim(),
                UserAgent = ValidateUserAgent(options.UserAgent),
                BasicAuthToken = authHeader,
                Segments = count,
                ScheduledAt = options.ScheduledAt,
                MaxBytesPerSecond = options.MaxBytesPerSecond is > 0 ? options.MaxBytesPerSecond : null,
                ResolveServerFileName = options.ResolveServerFileName && string.IsNullOrWhiteSpace(options.FileName),
                MediaQualityLabel = options.MediaQualityLabel,
                MediaSegmentUrls = options.MediaSegmentUrls is { Count: > 0 } ? [.. options.MediaSegmentUrls] : null,
                CreatedAt = DateTimeOffset.UtcNow,
                State = initialState
            };
            jobs.Add(job);
            try { Save(); } catch { jobs.Remove(job); throw; }
            Log(job, "added");
            return job.Id;
        }
    }

    public void Pause(string id)
    {
        lock (gate)
        {
            EnsureWritable(); var j = Find(id);
            if (j.State is DownloadState.Completed or DownloadState.Failed) return;
            j.State = DownloadState.Paused; j.Cancellation?.Cancel(); Save();
        }
    }

    public void Resume(string id)
    {
        lock (gate)
        {
            EnsureWritable(); var j = Find(id);
            if (j.State is DownloadState.Completed or DownloadState.Downloading) return;
            j.State = DownloadState.Queued; j.ScheduledAt = null; j.Error = null; Save();
        }
    }

    public void Redownload(string id)
    {
        lock (gate)
        {
            EnsureWritable(); var j = Find(id);
            j.Cancellation?.Cancel();
            if (j.Worker is null || j.Worker.IsCompleted)
            {
                if (File.Exists(Destination(j))) File.Delete(Destination(j));
                Reset(j);
                j.State = DownloadState.Queued;
                j.Error = null;
                j.CompletedAt = null;
                Save();
            }
        }
    }

    public void PauseAll()
    {
        lock (gate)
        {
            EnsureWritable();
            foreach (var j in jobs.Where(j => !j.Removed && j.State is DownloadState.Downloading or DownloadState.Queued or DownloadState.Scheduled))
            {
                j.State = DownloadState.Paused;
                j.Cancellation?.Cancel();
            }
            Save();
        }
    }

    public void ResumeAll()
    {
        lock (gate)
        {
            EnsureWritable();
            foreach (var j in jobs.Where(j => !j.Removed && j.State is DownloadState.Paused or DownloadState.Failed))
            {
                j.State = DownloadState.Queued;
                j.ScheduledAt = null;
                j.Error = null;
            }
            Save();
        }
    }

    public int ClearCompleted()
    {
        lock (gate)
        {
            EnsureWritable();
            var completed = jobs.Where(j => !j.Removed && j.State == DownloadState.Completed).ToList();
            foreach (var j in completed)
            {
                j.Removed = true;
                DeleteWorkspace(j);
                jobs.Remove(j);
            }
            if (completed.Count > 0) Save();
            return completed.Count;
        }
    }

    public void StartQueue(string queueName)
    {
        lock (gate)
        {
            EnsureWritable();
            var q = activeSettings.Queues.FirstOrDefault(x => string.Equals(x.Name, queueName, StringComparison.OrdinalIgnoreCase));
            if (q is not null) q.IsStarted = true;
            var s = Settings.Queues.FirstOrDefault(x => string.Equals(x.Name, queueName, StringComparison.OrdinalIgnoreCase));
            if (s is not null) s.IsStarted = true;
            foreach (var j in jobs.Where(j => !j.Removed && string.Equals(j.QueueName, queueName, StringComparison.OrdinalIgnoreCase) && j.State == DownloadState.Paused))
                j.State = DownloadState.Queued;
            Save(activeSettings);
        }
    }

    public void StopQueue(string queueName)
    {
        lock (gate)
        {
            EnsureWritable();
            var q = activeSettings.Queues.FirstOrDefault(x => string.Equals(x.Name, queueName, StringComparison.OrdinalIgnoreCase));
            if (q is not null) q.IsStarted = false;
            var s = Settings.Queues.FirstOrDefault(x => string.Equals(x.Name, queueName, StringComparison.OrdinalIgnoreCase));
            if (s is not null) s.IsStarted = false;
            foreach (var j in jobs.Where(j => !j.Removed && string.Equals(j.QueueName, queueName, StringComparison.OrdinalIgnoreCase) && j.State is DownloadState.Downloading or DownloadState.Queued))
            {
                j.State = DownloadState.Paused;
                j.Cancellation?.Cancel();
            }
            Save(activeSettings);
        }
    }

    public void UpdateJobProperties(string id, string? description, string? queueName, string? category, long? maxBytesPerSecond)
    {
        lock (gate)
        {
            EnsureWritable();
            var j = Find(id);
            if (description is not null) j.Description = description.Trim();
            if (!string.IsNullOrWhiteSpace(queueName)) j.QueueName = queueName.Trim();
            if (!string.IsNullOrWhiteSpace(category)) j.Category = category.Trim();
            j.MaxBytesPerSecond = maxBytesPerSecond is > 0 ? maxBytesPerSecond : null;
            Save();
        }
    }

    public void Remove(string id)
    {
        lock (gate)
        {
            EnsureWritable(); var j = Find(id); j.Removed = true; j.Cancellation?.Cancel(); Save();
            if (j.Worker is null || j.Worker.IsCompleted) { DeleteWorkspace(j); jobs.Remove(j); }
        }
    }

    public void Move(string id, int delta)
    {
        lock (gate)
        {
            EnsureWritable(); var j = Find(id); int index = jobs.IndexOf(j);
            int target = (int)Math.Clamp((long)index + delta, 0, jobs.Count - 1);
            jobs.RemoveAt(index); jobs.Insert(target, j); Save();
        }
    }

    public void SaveSettings()
    {
        lock (gate)
        {
            EnsureWritable();
            EnsureDefaultCollections(Settings);
            var validated = ValidateSettings(Settings);
            Save(validated);
            activeSettings = validated;
        }
    }

    public async Task<UrlProbeResult> ProbeUrlAsync(string url, CancellationToken token = default)
    {
        try
        {
            var uri = ValidateUrl(url);
            using var response = await SendAsync(uri.AbsoluteUri, 0, 0, null, null, null, null, null, token).ConfigureAwait(false);
            string effective = response.RequestMessage?.RequestUri?.AbsoluteUri ?? uri.AbsoluteUri;
            string? contentType = response.Content.Headers.ContentType?.MediaType;
            bool acceptsRanges = response.StatusCode == HttpStatusCode.PartialContent && ExactRange(response, 0, 0, out _);
            long? total = acceptsRanges
                ? response.Content.Headers.ContentRange?.Length
                : response.Content.Headers.ContentLength;

            string? serverFile = ExtractContentDispositionFileName(response);
            string suggestedName = !string.IsNullOrWhiteSpace(serverFile)
                ? SanitizeFileName(serverFile)
                : SiteGrabber.SafeSuggestedName(new Uri(effective));

            bool isHls = effective.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                         (contentType?.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ?? false);
            bool isDash = effective.Contains(".mpd", StringComparison.OrdinalIgnoreCase) ||
                          (contentType?.Contains("dash+xml", StringComparison.OrdinalIgnoreCase) ?? false);
            if (isHls && Path.GetExtension(suggestedName).Equals(".m3u8", StringComparison.OrdinalIgnoreCase))
                suggestedName = Path.ChangeExtension(suggestedName, ".ts");

            string category = isHls || isDash ? "Video" : ClassifyFileCategory(suggestedName, activeSettings.Categories);
            return new UrlProbeResult(
                response.IsSuccessStatusCode,
                effective,
                suggestedName,
                category,
                total,
                contentType,
                acceptsRanges,
                isHls,
                isDash,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return new UrlProbeResult(false, url, "download.bin", "General", null, null, false, false, false, ex.GetType().Name);
        }
    }

    public Task<IReadOnlyList<GrabbedSiteItem>> InspectSiteAsync(string pageUrl, CancellationToken token = default) =>
        SiteGrabber.InspectUrlAsync(client, pageUrl, activeSettings.Categories, token);

    public void ExportUrlList(string filePath)
    {
        string[] urls;
        lock (gate) urls = jobs.Where(j => !j.Removed).Select(j => j.Url).Distinct(StringComparer.Ordinal).ToArray();
        File.WriteAllLines(Path.GetFullPath(filePath), urls, Encoding.UTF8);
    }

    public void ExportFlugelFile(string filePath)
    {
        object[] items;
        lock (gate)
        {
            items = jobs.Where(j => !j.Removed).Select(j => new
            {
                j.Url,
                j.FileName,
                j.Category,
                j.QueueName,
                j.Description,
                j.Referrer,
                j.Segments
            }).ToArray();
        }
        File.WriteAllText(Path.GetFullPath(filePath), JsonSerializer.Serialize(new { Version = 1, ExportedAt = DateTimeOffset.UtcNow, Items = items }, Json), Encoding.UTF8);
    }

    public int ImportFromFile(string filePath, string? destinationDirectory = null, string queueName = "Main", bool startImmediately = false)
    {
        string full = Path.GetFullPath(filePath);
        string text = File.ReadAllText(full, Encoding.UTF8).Trim();
        string targetDir = destinationDirectory ?? activeSettings.DownloadDirectory;
        int added = 0;

        if (text.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("Items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsEl.EnumerateArray())
                {
                    string? url = item.TryGetProperty("Url", out var u) ? u.GetString() : null;
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    string? fileName = item.TryGetProperty("FileName", out var fn) ? fn.GetString() : null;
                    string? category = item.TryGetProperty("Category", out var cat) ? cat.GetString() : null;
                    string? desc = item.TryGetProperty("Description", out var d) ? d.GetString() : null;
                    string? referrer = item.TryGetProperty("Referrer", out var r) ? r.GetString() : null;
                    try
                    {
                        AddWithOptions(new AddDownloadOptions
                        {
                            Url = url,
                            DestinationDirectory = targetDir,
                            FileName = EnsureUniqueFileName(targetDir, fileName ?? SiteGrabber.SafeSuggestedName(new Uri(url))),
                            Category = category,
                            QueueName = queueName,
                            Description = desc ?? "",
                            Referrer = referrer,
                            StartImmediately = startImmediately
                        });
                        added++;
                    }
                    catch { }
                }
                return added;
            }
        }

        // Support IDM .ef2 format (< \r\n url \r\n referer: ... \r\n >) as well as plain URL lists
        foreach (string line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = ValidateUrl(line);
                    string suggested = EnsureUniqueFileName(targetDir, SiteGrabber.SafeSuggestedName(uri));
                    AddWithOptions(new AddDownloadOptions
                    {
                        Url = uri.AbsoluteUri,
                        DestinationDirectory = targetDir,
                        FileName = suggested,
                        QueueName = queueName,
                        StartImmediately = startImmediately
                    });
                    added++;
                }
                catch { }
            }
        }
        return added;
    }

    public string EnsureUniqueFileName(string destinationDirectory, string baseFileName)
    {
        string sanitized = SanitizeFileName(baseFileName);
        string dir = Path.GetFullPath(destinationDirectory);
        string stem = Path.GetFileNameWithoutExtension(sanitized);
        string ext = Path.GetExtension(sanitized);
        lock (gate)
        {
            string candidate = sanitized;
            for (int n = 1; n <= 999; n++)
            {
                string full = Path.Combine(dir, candidate);
                if (!File.Exists(full) && !Directory.Exists(full) &&
                    !jobs.Any(j => !j.Removed && string.Equals(Destination(j), full, StringComparison.OrdinalIgnoreCase)))
                {
                    return candidate;
                }
                candidate = $"{stem} ({n}){ext}";
            }
            return $"{stem}-{Guid.NewGuid().ToString("N")[..6]}{ext}";
        }
    }

    private bool IsQueueActiveNow(string queueName, out int queueMaxConcurrent)
    {
        queueMaxConcurrent = activeSettings.MaxConcurrent;
        var q = activeSettings.Queues.FirstOrDefault(x => string.Equals(x.Name, queueName, StringComparison.OrdinalIgnoreCase));
        if (q is null) return true;
        queueMaxConcurrent = Math.Clamp(q.MaxConcurrent, 1, 16);
        if (!q.IsStarted) return false;

        var now = DateTime.Now;
        if (q.ActiveDays is { Count: > 0 } && !q.ActiveDays.Contains(now.DayOfWeek))
            return false;

        if (q.EnableScheduleStart && TimeOnly.TryParse(q.DailyStartTime, out var startT))
        {
            var nowT = TimeOnly.FromDateTime(now);
            if (q.EnableScheduleStop && TimeOnly.TryParse(q.DailyStopTime, out var stopT))
            {
                if (startT <= stopT)
                {
                    if (nowT < startT || nowT > stopT) return false;
                }
                else
                {
                    // Overnight window (e.g., 23:00 to 07:30)
                    if (nowT < startT && nowT > stopT) return false;
                }
            }
            else if (nowT < startT)
            {
                return false;
            }
        }
        else if (q.EnableScheduleStop && TimeOnly.TryParse(q.DailyStopTime, out var stopOnly))
        {
            if (TimeOnly.FromDateTime(now) > stopOnly) return false;
        }

        return true;
    }

    private async Task ScheduleAsync()
    {
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                lock (gate)
                {
                    if (persistenceError is null)
                    {
                        int running = jobs.Count(j => j.Worker is { IsCompleted: false });
                        foreach (var j in jobs.Where(j => !j.Removed && (j.Worker is null || j.Worker.IsCompleted)))
                        {
                            if (running >= activeSettings.MaxConcurrent) break;
                            if (j.State == DownloadState.Scheduled && j.ScheduledAt <= DateTimeOffset.UtcNow) j.State = DownloadState.Queued;
                            if (j.State != DownloadState.Queued) continue;

                            if (!IsQueueActiveNow(j.QueueName, out int qLimit)) continue;
                            int runningInQueue = jobs.Count(x => !x.Removed && string.Equals(x.QueueName, j.QueueName, StringComparison.OrdinalIgnoreCase) && x.Worker is { IsCompleted: false });
                            if (runningInQueue >= qLimit) continue;

                            j.State = DownloadState.Downloading;
                            j.LastAttemptAt = DateTimeOffset.UtcNow;
                            j.Cancellation?.Dispose(); j.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                            int retries = activeSettings.MaxRetries;
                            j.Worker = Task.Run(() => WorkAsync(j, retries, j.Cancellation.Token)); running++;
                        }
                    }
                }
                await Task.Delay(100, shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
    }

    private async Task WorkAsync(Job job, int retries, CancellationToken token)
    {
        try
        {
            EnsureWorkspace(job);
            bool sequentialOnly = false;
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (job.MediaSegmentUrls is { Count: > 0 } || job.Url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                        await TransferMediaStreamAsync(job, token).ConfigureAwait(false);
                    else
                        await TransferAsync(job, sequentialOnly, token).ConfigureAwait(false);

                    token.ThrowIfCancellationRequested();
                    break;
                }
                catch (RangeRejectedException)
                {
                    lock (gate) Reset(job);
                    sequentialOnly = true;
                    Log(job, "range_fallback");
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    if (ex is PermanentHttpException || attempt >= retries) throw;
                    Log(job, "retry", attempt + 1);
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << Math.Min(attempt, 5))), token).ConfigureAwait(false);
                }
            }
            await PublishAsync(job, token).ConfigureAwait(false);
            Log(job, "completed");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            lock (gate) if (job.State == DownloadState.Downloading) job.State = DownloadState.Paused;
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                job.State = DownloadState.Failed;
                job.Error = ex is PermanentHttpException http ? http.CustomReason ?? $"HTTP {(int)http.Status}." :
                    ex is HttpRequestException network && network.StatusCode is { } status ? $"HTTP {(int)status}." :
                    ex is IOException ? "Transfer or destination I/O failed. Partial data retained." : "Transfer failed. Partial data retained.";
            }
            Log(job, "failed");
        }
        finally
        {
            lock (gate)
            {
                job.Speed = 0;
                if (job.Removed)
                {
                    try { DeleteWorkspace(job); }
                    catch (IOException) { Log(job, "cleanup_failed"); }
                    catch (UnauthorizedAccessException) { Log(job, "cleanup_failed"); }
                    jobs.Remove(job);
                }
                try { Save(); }
                catch (Exception ex)
                {
                    persistenceError = ex;
                    foreach (var j in jobs) j.Cancellation?.Cancel();
                    job.Error = "State persistence failed. Transfers stopped; original state preserved.";
                }
            }
        }
    }

    private async Task TransferMediaStreamAsync(Job job, CancellationToken token)
    {
        List<string>? segmentUrls;
        lock (gate) segmentUrls = job.MediaSegmentUrls is { Count: > 0 } ? [.. job.MediaSegmentUrls] : null;

        if (segmentUrls is null || segmentUrls.Count == 0)
        {
            using var manifestResp = await SendAsync(job.Url, null, null, null, job.Referrer, job.UserAgent, job.BasicAuthToken, job.CookieHeader, token).ConfigureAwait(false);
            CheckStatus(manifestResp);
            string manifestText = await manifestResp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var baseUri = manifestResp.RequestMessage?.RequestUri ?? new Uri(job.Url);
            var parsed = HlsAndDashParser.ParseHls(manifestText, baseUri);
            if (parsed.IsProtected)
                throw new PermanentHttpException(HttpStatusCode.Forbidden, "Protected or encrypted media stream is not supported.");

            if (parsed.IsMaster && parsed.Variants.Count > 0)
            {
                var chosen = parsed.Variants[0];
                if (chosen.IsProtected)
                    throw new PermanentHttpException(HttpStatusCode.Forbidden, "Protected or encrypted media stream is not supported.");
                using var childResp = await SendAsync(chosen.Url, null, null, null, job.Referrer, job.UserAgent, job.BasicAuthToken, job.CookieHeader, token).ConfigureAwait(false);
                CheckStatus(childResp);
                string childText = await childResp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                var childParsed = HlsAndDashParser.ParseHls(childText, new Uri(chosen.Url));
                if (childParsed.IsProtected)
                    throw new PermanentHttpException(HttpStatusCode.Forbidden, "Protected or encrypted media stream is not supported.");
                segmentUrls = childParsed.DirectSegments;
            }
            else
            {
                segmentUrls = parsed.DirectSegments;
            }

            if (segmentUrls.Count == 0)
                throw new IOException("No downloadable media segments found in manifest.");

            lock (gate) job.MediaSegmentUrls = [.. segmentUrls];
        }

        lock (gate)
        {
            Reset(job);
            job.Parts.Add(new Part { Start = 0, Length = long.MaxValue, Received = 0 });
            job.Started = Stopwatch.GetTimestamp();
            job.InitialReceived = 0;
        }
        EnsureWorkspace(job);
        var path = PartPath(job, 0);
        RejectLink(path);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[64 * 1024];

        for (int s = 0; s < segmentUrls.Count; s++)
        {
            token.ThrowIfCancellationRequested();
            using var response = await SendAsync(segmentUrls[s], null, null, null, job.Referrer, job.UserAgent, job.BasicAuthToken, job.CookieHeader, token).ConfigureAwait(false);
            CheckStatus(response);
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            while (true)
            {
                idle.CancelAfter(TimeSpan.FromSeconds(15));
                int read = await input.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                idle.CancelAfter(Timeout.InfiniteTimeSpan);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                lock (gate)
                {
                    job.Parts[0].Received += read;
                    job.Speed = job.Parts[0].Received / Math.Max(.001, Stopwatch.GetElapsedTime(job.Started).TotalSeconds);
                }
                await ApplySpeedThrottleAsync(job, token).ConfigureAwait(false);
            }
        }
        output.Flush(true);
        lock (gate)
        {
            job.Parts[0].Length = job.Parts[0].Received;
            job.Total = job.Parts[0].Received;
        }
    }

    private async Task TransferAsync(Job job, bool sequentialOnly, CancellationToken token)
    {
        using var probe = await SendAsync(job.Url, 0, 0, null, job.Referrer, job.UserAgent, job.BasicAuthToken, job.CookieHeader, token).ConfigureAwait(false);
        CheckStatus(probe);
        bool range = probe.StatusCode == HttpStatusCode.PartialContent && ExactRange(probe, 0, 0, out long length);
        long? total = range ? probe.Content.Headers.ContentRange!.Length : probe.StatusCode == HttpStatusCode.OK ? probe.Content.Headers.ContentLength : null;
        string? validator = Validator(probe);
        string effectiveUrl = probe.RequestMessage!.RequestUri!.AbsoluteUri;
        bool segmented = !sequentialOnly && range && validator is not null && total > 0;
        string? dispName = job.ResolveServerFileName ? ExtractContentDispositionFileName(probe) : null;

        lock (gate)
        {
            if (!string.IsNullOrWhiteSpace(dispName))
            {
                string sanitized = SanitizeFileName(dispName);
                if (!string.Equals(job.FileName, sanitized, StringComparison.OrdinalIgnoreCase))
                    job.FileName = EnsureUniqueFileName(job.DestinationDirectory, sanitized);
                job.ResolveServerFileName = false;
            }
            if (!segmented || job.Validator != validator || job.Total != total || job.EffectiveUrl != effectiveUrl || !ValidParts(job)) Reset(job);
            job.Total = total; job.Validator = validator; job.EffectiveUrl = effectiveUrl;
            if (job.Parts.Count == 0)
            {
                int count = segmented ? (int)Math.Min(job.Segments, total!.Value) : 1;
                long start = 0;
                for (int i = 0; i < count; i++)
                {
                    long size = total is { } t ? t / count + (i < t % count ? 1 : 0) : long.MaxValue;
                    job.Parts.Add(new Part { Start = start, Length = size }); start += size;
                }
            }
            job.Started = Stopwatch.GetTimestamp(); job.InitialReceived = job.Parts.Sum(p => p.Received);
        }
        EnsureWorkspace(job);
        if (segmented)
        {
            using var siblings = CancellationTokenSource.CreateLinkedTokenSource(token);
            var tasks = job.Parts.Select((p, index) => FetchGuardedAsync(job, p, index, siblings)).ToArray();
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch
            {
                if (tasks.Any(t => t.Exception?.Flatten().InnerExceptions.Any(e => e is RangeRejectedException) == true))
                    throw new RangeRejectedException();
                throw;
            }
        }
        else await FetchAsync(job, job.Parts[0], 0, false, token).ConfigureAwait(false);
    }

    private async Task FetchGuardedAsync(Job job, Part part, int index, CancellationTokenSource siblings)
    {
        try { await FetchAsync(job, part, index, true, siblings.Token).ConfigureAwait(false); }
        catch { siblings.Cancel(); throw; }
    }

    private async Task FetchAsync(Job job, Part part, int index, bool range, CancellationToken token)
    {
        if (range && part.Received == part.Length) return;
        long start = part.Start + part.Received;
        using var response = await SendAsync(job.Url, range ? start : null, range ? part.Start + part.Length - 1 : null,
            range ? job.Validator : null, job.Referrer, job.UserAgent, job.BasicAuthToken, job.CookieHeader, token).ConfigureAwait(false);
        if (range && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) throw new RangeRejectedException();
        CheckStatus(response);
        if (response.Content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Unexpected content encoding.");
        if (range && (response.StatusCode != HttpStatusCode.PartialContent || !ExactRange(response, start, part.Start + part.Length - 1, out var total) ||
            total != job.Total || Validator(response) != job.Validator || response.RequestMessage!.RequestUri!.AbsoluteUri != job.EffectiveUrl))
            throw new RangeRejectedException();
        if (!range)
        {
            if (response.StatusCode != HttpStatusCode.OK) throw new IOException("Unexpected response.");
            lock (gate)
            {
                part.Received = 0;
                job.Total = response.Content.Headers.ContentLength;
                part.Length = job.Total ?? long.MaxValue;
            }
        }
        var path = PartPath(job, index);
        RejectLink(path);
        await using var output = new FileStream(path, range ? FileMode.OpenOrCreate : FileMode.Create, FileAccess.Write, FileShare.Read,
            1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (range && output.Length != part.Received) throw new IOException("Partial length changed.");
        output.Position = part.Received;
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        byte[] buffer = new byte[64 * 1024];
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
        while (true)
        {
            idle.CancelAfter(TimeSpan.FromSeconds(15));
            int read = await input.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
            idle.CancelAfter(Timeout.InfiniteTimeSpan);
            if (read == 0) break;
            if (read > part.Length - part.Received) throw new IOException("Body exceeds declared length.");
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            lock (gate)
            {
                part.Received += read;
                job.Speed = (job.Parts.Sum(p => p.Received) - job.InitialReceived) / Math.Max(.001, Stopwatch.GetElapsedTime(job.Started).TotalSeconds);
            }
            await ApplySpeedThrottleAsync(job, token).ConfigureAwait(false);
            if (Stopwatch.GetElapsedTime(job.LastCheckpoint).TotalSeconds >= 1)
            {
                output.Flush(true);
                lock (gate)
                {
                    if (Stopwatch.GetElapsedTime(job.LastCheckpoint).TotalSeconds >= 1)
                    { Save(); job.LastCheckpoint = Stopwatch.GetTimestamp(); }
                }
            }
        }
        if (part.Length != long.MaxValue && part.Received != part.Length) throw new IOException("Early EOF.");
        output.Flush(true);
        if (part.Length == long.MaxValue) lock (gate) { part.Length = part.Received; job.Total = part.Received; }
    }

    private async Task ApplySpeedThrottleAsync(Job job, CancellationToken token)
    {
        long? limit = null;
        double currentSpeed;
        long bytesThisSession;
        double elapsedSec;
        lock (gate)
        {
            if (job.MaxBytesPerSecond is > 0)
                limit = job.MaxBytesPerSecond.Value;
            if (activeSettings.SpeedLimiterEnabled && activeSettings.MaxGlobalBytesPerSecond > 0)
                limit = limit is { } l ? Math.Min(l, activeSettings.MaxGlobalBytesPerSecond) : activeSettings.MaxGlobalBytesPerSecond;
            if (limit is null or <= 0) return;

            bytesThisSession = Math.Max(0, job.Parts.Sum(p => p.Received) - job.InitialReceived);
            elapsedSec = Math.Max(0.001, Stopwatch.GetElapsedTime(job.Started).TotalSeconds);
            currentSpeed = bytesThisSession / elapsedSec;
        }
        if (currentSpeed > limit.Value)
        {
            double targetSeconds = (double)bytesThisSession / limit.Value;
            int delayMs = (int)Math.Clamp((targetSeconds - elapsedSec) * 1000.0, 10, 500);
            await Task.Delay(delayMs, token).ConfigureAwait(false);
            lock (gate)
            {
                job.Speed = (job.Parts.Sum(p => p.Received) - job.InitialReceived) / Math.Max(.001, Stopwatch.GetElapsedTime(job.Started).TotalSeconds);
            }
        }
    }

    internal static string? ValidateCookieHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string trimmed = value.Trim();
        if (trimmed.Length > 8192 || trimmed.Any(c => c is '\r' or '\n' or '\0' || char.IsControl(c)))
            throw new ArgumentException("Cookie header is too long or contains control characters.");
        if (!trimmed.Contains('=')) throw new ArgumentException("Cookie header must contain name=value pairs.");
        return trimmed;
    }

    private static string? ValidateUserAgent(string? value)
    {
        if (value is null) return null;
        if (value.Length > 2048 || value.Any(char.IsControl))
            throw new ArgumentException("Invalid user agent.");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task<HttpResponseMessage> SendAsync(
        string url,
        long? from,
        long? to,
        string? validator,
        string? referrer,
        string? userAgent,
        string? basicAuthToken,
        string? cookieHeader,
        CancellationToken token)
    {
        for (int redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(cookieHeader))
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            if (from is { } start) request.Headers.Range = new RangeHeaderValue(start, to);
            if (validator is not null) request.Headers.TryAddWithoutValidation("If-Range", validator);
            if (!string.IsNullOrWhiteSpace(referrer) && Uri.TryCreate(referrer, UriKind.Absolute, out var refUri))
                request.Headers.Referrer = refUri;
            if (!string.IsNullOrWhiteSpace(userAgent))
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            if (!string.IsNullOrWhiteSpace(basicAuthToken) && Uri.TryCreate(url, UriKind.Absolute, out var reqUri))
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuthToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is not { } location)
                return response;
            try
            {
                if (redirects >= 8) throw new HttpRequestException("Too many redirects.");
                var previous = new Uri(url);
                var next = ValidateUrl(new Uri(previous, location).AbsoluteUri);
                if (previous.Scheme == "https" && next.Scheme == "http")
                    throw new HttpRequestException("HTTPS downgrade redirect rejected.");
                if (!string.Equals(previous.GetLeftPart(UriPartial.Authority), next.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
                {
                    cookieHeader = null;
                    basicAuthToken = null;
                    referrer = null;
                }
                url = next.AbsoluteUri;
            }
            finally { response.Dispose(); }
        }
    }

    private static string? ExtractContentDispositionFileName(HttpResponseMessage response)
    {
        var cd = response.Content.Headers.ContentDisposition;
        string? raw = cd?.FileNameStar ?? cd?.FileName;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().Trim('"', '\'');
    }

    private static void CheckStatus(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && response.Content.Headers.ContentRange?.Length == 0) return;
        if ((int)response.StatusCode is 408 or 429 or >= 500) throw new HttpRequestException("Transient HTTP failure.", null, response.StatusCode);
        throw new PermanentHttpException(response.StatusCode);
    }

    private static bool ExactRange(HttpResponseMessage response, long start, long end, out long length)
    {
        var r = response.Content.Headers.ContentRange; length = r?.Length ?? -1;
        return r?.Unit == "bytes" && r.From == start && r.To == end && length > end &&
            (response.Content.Headers.ContentLength is null || response.Content.Headers.ContentLength == end - start + 1);
    }

    private static string? Validator(HttpResponseMessage response) => response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() :
        response.Content.Headers.LastModified is { } date && date <= DateTimeOffset.UtcNow ? date.ToString("R") : null;

    private async Task PublishAsync(Job job, CancellationToken token)
    {
        var assembled = Path.Combine(Workspace(job), "assembled.tmp");
        RejectLink(assembled);
        await using (var target = new FileStream(assembled, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
        {
            for (int i = 0; i < job.Parts.Count; i++)
            {
                var part = job.Parts[i];
                if (part.Received != part.Length) throw new IOException("Incomplete part.");
                await using var source = new FileStream(PartPath(job, i), FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
                if (source.Length != part.Length) throw new IOException("Partial length changed.");
                await source.CopyToAsync(target, 64 * 1024, token).ConfigureAwait(false);
            }
            target.Flush(true);
        }
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            File.Move(assembled, Destination(job), false);
            job.State = DownloadState.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Error = null;
        }
        try { DeleteWorkspace(job); }
        catch (IOException) { Log(job, "cleanup_failed"); }
        catch (UnauthorizedAccessException) { Log(job, "cleanup_failed"); }
    }

    public static string ClassifyFileCategory(string fileName, IReadOnlyList<CategoryRule>? rules = null)
    {
        string ext = Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return "General";
        var effectiveRules = rules is { Count: > 0 } ? rules : CategoryRule.CreateDefaults();
        foreach (var rule in effectiveRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Extensions)) continue;
            var tokens = rule.Extensions.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Any(t => string.Equals(t.TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase)))
                return rule.Name;
        }
        return "General";
    }

    public static string SanitizeFileName(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return "download.bin";
        string name = Path.GetFileName(candidate.Trim().Replace('\\', '/'));
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (c >= 32 && !"<>:\"/\\|?*".Contains(c))
                sb.Append(c);
            else
                sb.Append('_');
        }
        string cleaned = sb.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "download.bin";
        if (cleaned.Length > 200)
        {
            string ext = Path.GetExtension(cleaned);
            cleaned = Path.GetFileNameWithoutExtension(cleaned)[..Math.Min(180, cleaned.Length)] + ext;
        }
        try { ValidateName(cleaned); return cleaned; }
        catch { return "_" + cleaned; }
    }

    private static Uri ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Absolute HTTP(S) URL without embedded credentials required.", nameof(url));
        return uri;
    }

    private static void ValidateName(string name)
    {
        string stem = name.Split('.')[0].TrimEnd(' ');
        if (string.IsNullOrWhiteSpace(name) || name.Length > 240 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)) ||
            new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3]))
            throw new ArgumentException("Safe non-reserved filename required.", nameof(name));
    }

    private static void EnsureDefaultCollections(ManagerSettings settings)
    {
        if (settings.Categories is null || settings.Categories.Count == 0)
            settings.Categories = CategoryRule.CreateDefaults();
        if (settings.Queues is null || settings.Queues.Count == 0)
            settings.Queues = QueueDefinition.CreateDefaults();
        if (!settings.Queues.Any(q => string.Equals(q.Name, "Main", StringComparison.OrdinalIgnoreCase)))
            settings.Queues.Insert(0, new QueueDefinition { Name = "Main", IsStarted = true, MaxConcurrent = 4 });
    }

    private static ManagerSettings ValidateSettings(ManagerSettings settings)
    {
        if (settings.MaxConcurrent is < 1 or > 16 || settings.Segments is < 1 or > 16 || settings.MaxRetries is < 0 or > 10 || string.IsNullOrWhiteSpace(settings.DownloadDirectory))
            throw new ArgumentException("Concurrency and segments must be 1–16; retries 0–10; directory required.");
        if (settings.MaxGlobalBytesPerSecond < 0)
            throw new ArgumentException("Global speed limit cannot be negative.");
        return new ManagerSettings
        {
            DownloadDirectory = Path.GetFullPath(settings.DownloadDirectory),
            MaxConcurrent = settings.MaxConcurrent,
            Segments = settings.Segments,
            MaxRetries = settings.MaxRetries,
            UseCategorySubfolders = settings.UseCategorySubfolders,
            SpeedLimiterEnabled = settings.SpeedLimiterEnabled,
            MaxGlobalBytesPerSecond = settings.MaxGlobalBytesPerSecond,
            ShowCompletionDialog = settings.ShowCompletionDialog,
            StartQueueImmediately = settings.StartQueueImmediately,
            MinimizeToTray = settings.MinimizeToTray,
            MonitorClipboard = settings.MonitorClipboard,
            BrowserAutoIntercept = settings.BrowserAutoIntercept,
            BrowserVideoBalloonEnabled = settings.BrowserVideoBalloonEnabled,
            AutoCaptureExtensions = settings.AutoCaptureExtensions ?? "",
            ExcludedHosts = settings.ExcludedHosts ?? "",
            ProxyMode = settings.ProxyMode ?? "System",
            ProxyAddress = settings.ProxyAddress,
            ProxyUsername = settings.ProxyUsername,
            Categories = settings.Categories.Select(c => new CategoryRule
            {
                Name = string.IsNullOrWhiteSpace(c.Name) ? "General" : c.Name.Trim(),
                Extensions = c.Extensions ?? "",
                Subfolder = c.Subfolder ?? ""
            }).ToList(),
            Queues = settings.Queues.Select(q => new QueueDefinition
            {
                Name = string.IsNullOrWhiteSpace(q.Name) ? "Main" : q.Name.Trim(),
                IsStarted = q.IsStarted,
                MaxConcurrent = Math.Clamp(q.MaxConcurrent, 1, 16),
                EnableScheduleStart = q.EnableScheduleStart,
                DailyStartTime = q.DailyStartTime ?? "00:00",
                EnableScheduleStop = q.EnableScheduleStop,
                DailyStopTime = q.DailyStopTime ?? "23:59",
                ActiveDays = q.ActiveDays is { Count: > 0 } ? [.. q.ActiveDays] : [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday]
            }).ToList()
        };
    }

    private Job Find(string id) => jobs.FirstOrDefault(j => j.Id == id && !j.Removed) ?? throw new KeyNotFoundException("Unknown download ID.");
    private void EnsureWritable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (persistenceError is not null) throw new IOException("State persistence unavailable; transfers stopped.", persistenceError);
    }
    private static string Destination(Job j) => Path.Combine(j.DestinationDirectory, j.FileName);
    private static string Workspace(Job j) => Path.Combine(j.DestinationDirectory, ".flugel-download-manager-" + j.Id);
    private static string PartPath(Job j, int index) => Path.Combine(Workspace(j), index + ".part");
    private static void RejectLink(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Unsafe partial file.");
    }
    private static void EnsureWorkspace(Job j)
    {
        Directory.CreateDirectory(Workspace(j));
        if ((File.GetAttributes(Workspace(j)) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unsafe partial directory.");
    }
    private static bool ValidParts(Job j)
    {
        if (j.Parts.Count == 0) return true;
        for (int i = 0; i < j.Parts.Count; i++)
        {
            var p = j.Parts[i];
            var path = PartPath(j, i);
            RejectLink(path);
            if (!File.Exists(path) ? p.Received != 0 : new FileInfo(path).Length != p.Received) return false;
        }
        return true;
    }
    private static bool ValidPartMetadata(Job j)
    {
        long start = 0;
        foreach (var p in j.Parts)
        {
            if (p.Start != start || p.Length > long.MaxValue - start) return false;
            start += p.Length;
        }
        return (j.Parts.Count == 0 || j.Total is null || start == j.Total) &&
            (j.State != DownloadState.Completed || j.Total is not null && j.Parts.Count > 0 && j.Parts.All(p => p.Received == p.Length));
    }
    private static void Reset(Job j)
    {
        DeleteWorkspace(j); j.Parts.Clear(); j.Validator = null; j.Total = null; j.EffectiveUrl = null;
    }
    private static void DeleteWorkspace(Job j)
    {
        var path = Workspace(j);
        if (!Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unsafe partial directory.");
        for (int i = 0; i < 16; i++) if (File.Exists(PartPath(j, i))) File.Delete(PartPath(j, i));
        if (File.Exists(Path.Combine(path, "assembled.tmp"))) File.Delete(Path.Combine(path, "assembled.tmp"));
        if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
    }

    private void Save(ManagerSettings? settings = null)
    {
        var path = Path.Combine(directory, "state.json");
        var temp = Path.Combine(directory, "state.tmp");
        var state = new SavedState { Settings = settings ?? activeSettings, Jobs = jobs.Where(j => !j.Removed).ToList() };
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, state, Json); stream.Flush(true); }
        File.Move(temp, path, true);
    }

    private void Log(Job job, string action, int? retry = null)
    {
        try
        {
            lock (gate) File.AppendAllText(Path.Combine(directory, "events.jsonl"), JsonSerializer.Serialize(new
            { at = DateTimeOffset.UtcNow, id = job.Id, action, retry }) + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] workers;
        lock (gate)
        {
            if (disposed) return;
            disposed = true; shutdown.Cancel();
            workers = jobs.Where(j => j.Worker is not null).Select(j => j.Worker!).ToArray();
        }
        try
        {
            await scheduler.ConfigureAwait(false);
            await Task.WhenAll(workers).ConfigureAwait(false);
            lock (gate) Save();
        }
        finally
        {
            client.Dispose(); instanceLock.Dispose(); shutdown.Dispose();
            foreach (var j in jobs) j.Cancellation?.Dispose();
        }
    }

    private sealed class SavedState
    {
        public SavedState() { }
        [System.Text.Json.Serialization.JsonRequired]
        public int Version { get; set; } = 1;
        [System.Text.Json.Serialization.JsonRequired]
        public ManagerSettings Settings { get; set; } = new();
        [System.Text.Json.Serialization.JsonRequired]
        public List<Job> Jobs { get; set; } = [];
    }
    private sealed class Job
    {
        public Job() { }
        [System.Text.Json.Serialization.JsonRequired]
        public string Id { get; set; } = "";
        [System.Text.Json.Serialization.JsonRequired]
        public string Url { get; set; } = "";
        [System.Text.Json.Serialization.JsonRequired]
        public string FileName { get; set; } = "";
        [System.Text.Json.Serialization.JsonRequired]
        public string DestinationDirectory { get; set; } = "";
        [System.Text.Json.Serialization.JsonRequired]
        public DownloadState State { get; set; }
        public DateTimeOffset? ScheduledAt { get; set; }
        [System.Text.Json.Serialization.JsonRequired]
        public int Segments { get; set; }
        public long? Total { get; set; }
        public string? Validator { get; set; }
        public string? EffectiveUrl { get; set; }
        public string? Error { get; set; }
        public string Category { get; set; } = "General";
        public string QueueName { get; set; } = "Main";
        public string Description { get; set; } = "";
        public string? Referrer { get; set; }
        public string? UserAgent { get; set; }
        public long? MaxBytesPerSecond { get; set; }
        public bool ResolveServerFileName { get; set; }
        public string? MediaQualityLabel { get; set; }
        public List<string>? MediaSegmentUrls { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastAttemptAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        [System.Text.Json.Serialization.JsonRequired]
        public List<Part> Parts { get; set; } = [];
        [System.Text.Json.Serialization.JsonIgnore] public string? BasicAuthToken { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? CookieHeader { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public Task? Worker { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public CancellationTokenSource? Cancellation { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public bool Removed { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public double Speed { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public long Started { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public long InitialReceived { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public long LastCheckpoint { get; set; } = Stopwatch.GetTimestamp();
    }
    private sealed class Part
    {
        public Part() { }
        [System.Text.Json.Serialization.JsonRequired]
        public long Start { get; set; }
        [System.Text.Json.Serialization.JsonRequired]
        public long Length { get; set; }
        [System.Text.Json.Serialization.JsonRequired]
        public long Received { get; set; }
    }
    private sealed class RangeRejectedException : IOException;
    private sealed class PermanentHttpException(HttpStatusCode status, string? customReason = null) : HttpRequestException
    {
        public HttpStatusCode Status { get; } = status;
        public string? CustomReason { get; } = customReason;
    }
}
