using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Harbor.Core;

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
                if (!destinations.Add(Destination(job))) throw new InvalidDataException("Duplicate destination.");
                if (job.State == DownloadState.Downloading) job.State = DownloadState.Paused;
                jobs.Add(job);
            }
            // Revalidate partial lengths in the worker, not here. Windows can briefly retain
            // killed-process file handles; normal bounded I/O retries cover that release window.
            client = new HttpClient(new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.None, MaxAutomaticRedirections = 8,
                UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(15), MaxConnectionsPerServer = 64
            }) { Timeout = Timeout.InfiniteTimeSpan };
            scheduler = Task.Run(ScheduleAsync);
        }
        catch { instanceLock.Dispose(); throw; }
    }

    public IReadOnlyList<DownloadSnapshot> GetSnapshots()
    {
        lock (gate) return jobs.Where(j => !j.Removed).Select(j => new DownloadSnapshot(j.Id, j.Url, j.FileName,
            Destination(j), j.State, j.Parts.Sum(p => p.Received), j.Total,
            j.State == DownloadState.Downloading ? j.Speed : 0, j.Error, j.ScheduledAt, j.Segments)).ToArray();
    }

    public string Add(string url, string destinationDirectory, string? fileName = null, DateTimeOffset? scheduledAt = null, int? segments = null)
    {
        var uri = ValidateUrl(url);
        fileName ??= Uri.UnescapeDataString(uri.AbsolutePath.Split('/').LastOrDefault() ?? "");
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "download.bin";
        ValidateName(fileName);
        var destination = Path.GetFullPath(destinationDirectory);
        lock (gate)
        {
            EnsureWritable();
            int count = segments ?? activeSettings.Segments;
            if (count is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(segments));
            string target = Path.Combine(destination, fileName);
            if (File.Exists(target) || Directory.Exists(target) || jobs.Any(j => !j.Removed && string.Equals(Destination(j), target, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Destination already exists or is reserved by another download.");
            Directory.CreateDirectory(destination);
            var job = new Job { Id = Guid.NewGuid().ToString("N"), Url = uri.AbsoluteUri, DestinationDirectory = destination,
                FileName = fileName, Segments = count, ScheduledAt = scheduledAt,
                State = scheduledAt > DateTimeOffset.UtcNow ? DownloadState.Scheduled : DownloadState.Queued };
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
        lock (gate) { EnsureWritable(); var validated = ValidateSettings(Settings); Save(validated); activeSettings = validated; }
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
                            j.State = DownloadState.Downloading;
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
                    await TransferAsync(job, sequentialOnly, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    break;
                }
                catch (RangeRejectedException)
                {
                    lock (gate) Reset(job);
                    sequentialOnly = true;
                    Log(job, "range_fallback");
                    // One safe sequential fallback is not a network retry.
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    if (ex is PermanentHttpException || attempt >= retries) throw;
                    Log(job, "retry", attempt + 1);
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << Math.Min(attempt, 5))), token).ConfigureAwait(false);
                }
            }
            // Publishing is not a network operation. Never redownload on a disk error or collision.
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
                // Never surface exception messages: framework errors may embed signed URLs.
                job.Error = ex is PermanentHttpException http ? $"HTTP {(int)http.Status}." :
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

    private async Task TransferAsync(Job job, bool sequentialOnly, CancellationToken token)
    {
        using var probe = await SendAsync(job.Url, 0, 0, null, token).ConfigureAwait(false);
        CheckStatus(probe);
        bool range = probe.StatusCode == HttpStatusCode.PartialContent && ExactRange(probe, 0, 0, out long length);
        long? total = range ? probe.Content.Headers.ContentRange!.Length : probe.StatusCode == HttpStatusCode.OK ? probe.Content.Headers.ContentLength : null;
        string? validator = Validator(probe);
        string effectiveUrl = probe.RequestMessage!.RequestUri!.AbsoluteUri;
        bool segmented = !sequentialOnly && range && validator is not null && total > 0;
        lock (gate)
        {
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
                // A cancelled lower-index sibling must not hide the response that rejected ranges.
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
            range ? job.Validator : null, token).ConfigureAwait(false);
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

    private async Task<HttpResponseMessage> SendAsync(string url, long? from, long? to, string? validator, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        if (from is { } start) request.Headers.Range = new RangeHeaderValue(start, to);
        if (validator is not null) request.Headers.TryAddWithoutValidation("If-Range", validator);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
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
            job.State = DownloadState.Completed; job.Error = null;
        }
        try { DeleteWorkspace(job); }
        catch (IOException) { Log(job, "cleanup_failed"); }
        catch (UnauthorizedAccessException) { Log(job, "cleanup_failed"); }
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

    private static ManagerSettings ValidateSettings(ManagerSettings settings)
    {
        if (settings.MaxConcurrent is < 1 or > 16 || settings.Segments is < 1 or > 16 || settings.MaxRetries is < 0 or > 10 || string.IsNullOrWhiteSpace(settings.DownloadDirectory))
            throw new ArgumentException("Concurrency and segments must be 1–16; retries 0–10; directory required.");
        return new ManagerSettings { DownloadDirectory = Path.GetFullPath(settings.DownloadDirectory), MaxConcurrent = settings.MaxConcurrent,
            Segments = settings.Segments, MaxRetries = settings.MaxRetries };
    }

    private Job Find(string id) => jobs.FirstOrDefault(j => j.Id == id && !j.Removed) ?? throw new KeyNotFoundException("Unknown download ID.");
    private void EnsureWritable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (persistenceError is not null) throw new IOException("State persistence unavailable; transfers stopped.", persistenceError);
    }
    private static string Destination(Job j) => Path.Combine(j.DestinationDirectory, j.FileName);
    private static string Workspace(Job j) => Path.Combine(j.DestinationDirectory, ".streamharbor-" + j.Id);
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
        // Delete only engine-owned files, never recursive directory trees.
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
        catch (IOException) { /* Logging cannot invalidate a safe transfer. */ }
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
        [System.Text.Json.Serialization.JsonRequired]
        public List<Part> Parts { get; set; } = [];
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
    private sealed class PermanentHttpException(HttpStatusCode status) : HttpRequestException
    { public HttpStatusCode Status { get; } = status; }
}
