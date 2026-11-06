using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Harbor.Core;

if (args is ["--crash-child", var childState, var childOutput, var childUrl, var marker])
{
    await using var childManager = new DownloadManager(childState);
    string childId = childManager.Add(childUrl, childOutput, "crash.bin");
    await Task.Delay(1600);
    await File.WriteAllTextAsync(marker, childId);
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}

var root = Path.Combine(Path.GetTempPath(), "opencode", "harbor-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
await using var server = new TestServer();
var results = new List<string>();
int failed = 0;
async Task Check(string name, Func<Task> test)
{
    var clock = Stopwatch.StartNew();
    try { await test(); results.Add($"PASS {name} ({clock.Elapsed.TotalSeconds:0.00}s)"); }
    catch (Exception ex) { failed++; results.Add($"FAIL {name}: {ex}"); }
    Console.WriteLine(results[^1]);
}
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
(string state, string output) Paths(string name)
{
    var dir = Path.Combine(root, name); Directory.CreateDirectory(dir);
    var output = Path.Combine(dir, "output"); Directory.CreateDirectory(output);
    return (Path.Combine(dir, "state"), output);
}
async Task<DownloadSnapshot> Wait(DownloadManager manager, string id, Func<DownloadSnapshot, bool>? predicate = null, int seconds = 30)
{
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed.TotalSeconds < seconds)
    {
        var s = manager.GetSnapshots().Single(j => j.Id == id);
        if ((predicate ?? (s => s.State is DownloadState.Completed or DownloadState.Failed))(s)) return s;
        await Task.Delay(20);
    }
    throw new TimeoutException($"Job {id}: {manager.GetSnapshots().Single(j => j.Id == id)}");
}
async Task Verify(DownloadSnapshot snapshot, long size, int version = 1)
{
    Assert(snapshot.State == DownloadState.Completed, $"Expected complete: {snapshot.State}, {snapshot.Error}");
    Assert(new FileInfo(snapshot.Destination).Length == size, "Length mismatch");
    using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    byte[] buffer = new byte[65536];
    for (long offset = 0; offset < size; offset += buffer.Length)
    {
        int count = (int)Math.Min(buffer.Length, size - offset); TestServer.Fill(buffer.AsSpan(0, count), offset, version);
        expected.AppendData(buffer, 0, count);
    }
    await using var actual = File.OpenRead(snapshot.Destination);
    Assert((await SHA256.HashDataAsync(actual)).SequenceEqual(expected.GetHashAndReset()), "SHA-256 mismatch");
}
async Task DownloadCase(string name, string route, long size, int segments = 4, int version = 1)
{
    var paths = Paths(name);
    await using var manager = new DownloadManager(paths.state);
    var id = manager.Add(server.Url(route), paths.output, "payload.bin", segments: segments);
    await Verify(await Wait(manager, id), size, version);
}

await Check("sequential known length", () => DownloadCase("sequential", "sequential", TestServer.NormalSize, 1));
await Check("segmented exact ranges", async () =>
{
    await DownloadCase("segmented", "range", TestServer.NormalSize);
    Assert(server.Requests.Any(r => r.Path == "range" && r.From > 0), "No disjoint range observed");
});
await Check("large bounded streaming 64 MiB", () => DownloadCase("large", "large", TestServer.LargeSize, 8));
await Check("ignored ranges restart sequential", () => DownloadCase("ignored", "ignored", TestServer.NormalSize));
await Check("false Accept-Ranges ignored", () => DownloadCase("false", "false", TestServer.NormalSize));
await Check("malformed Content-Range", () => DownloadCase("malformed", "malformed", TestServer.NormalSize));
await Check("wrong segmented Content-Range fallback", () => DownloadCase("wrong", "wrong", TestServer.NormalSize));
await Check("range rejection fallback", () => DownloadCase("reject", "reject", TestServer.NormalSize));
await Check("416 changed range fallback", () => DownloadCase("416", "416", TestServer.NormalSize));
await Check("changed validator cannot mix bytes", () => DownloadCase("changed", "changed", TestServer.NormalSize, version: 2));
await Check("weak validator forces sequential", async () =>
{
    await DownloadCase("weak", "weak", TestServer.NormalSize);
    Assert(!server.Requests.Any(r => r.Path == "weak" && r.From > 0), "Weak validator used for ranges");
});
await Check("Last-Modified validator", () => DownloadCase("modified", "modified", TestServer.NormalSize));
await Check("unknown chunked length", () => DownloadCase("unknown", "unknown", TestServer.NormalSize));
await Check("empty zero bytes", () => DownloadCase("empty", "empty", 0));
await Check("finite redirect", () => DownloadCase("redirect", "redirect", TestServer.NormalSize));
await Check("dropped body retries", () => DownloadCase("drop", "drop", TestServer.NormalSize));
await Check("503 bounded retry then success", () => DownloadCase("recover", "recover", TestServer.NormalSize));
await Check("404 permanent failure without retries", async () =>
{
    var p = Paths("404"); await using var m = new DownloadManager(p.state);
    var s = await Wait(m, m.Add(server.Url("404"), p.output, "bad.bin"));
    Assert(s.State == DownloadState.Failed && s.Error == "HTTP 404.", "404 not surfaced");
    Assert(server.Requests.Count(r => r.Path == "404") == 1, "404 retried");
    Assert(!File.Exists(s.Destination), "Failure published");
});
await Check("503 exhausted bounded retries", async () =>
{
    var p = Paths("503"); await using var m = new DownloadManager(p.state);
    m.Settings.MaxRetries = 2; m.SaveSettings();
    var s = await Wait(m, m.Add(server.Url("503"), p.output, "bad.bin"));
    Assert(s.State == DownloadState.Failed, "503 not failed");
    Assert(server.Requests.Count(r => r.Path == "503") == 3, "Incorrect retry bound");
});
await Check("pause resume retains validated ranges", async () =>
{
    var p = Paths("pause"); await using var m = new DownloadManager(p.state);
    string id = m.Add(server.Url("slow-pause"), p.output, "slow.bin");
    var progress = await Wait(m, id, s => s.Received > 256 * 1024);
    m.Pause(id); await Task.Delay(200);
    long received = m.GetSnapshots().Single().Received; await Task.Delay(150);
    Assert(m.GetSnapshots().Single().Received == received, "Pause still receiving");
    Assert(!File.Exists(progress.Destination), "Pause published");
    int count = server.Requests.Count; m.Resume(id);
    await Verify(await Wait(m, id), TestServer.SlowSize);
    Assert(server.Requests.Skip(count).Any(r => r.Path == "slow-pause" && r.From > 0 && r.From % (TestServer.SlowSize / 4) != 0), "Resume restarted all bytes");
});
await Check("restart mid-transfer revalidates partials", async () =>
{
    var p = Paths("restart"); string id;
    await using (var m = new DownloadManager(p.state))
    {
        id = m.Add(server.Url("slow-restart"), p.output, "restart.bin");
        await Wait(m, id, s => s.Received > 256 * 1024);
    }
    await using (var m = new DownloadManager(p.state))
    {
        Assert(m.GetSnapshots().Single().State == DownloadState.Paused, "Restart auto-ran interrupted job");
        m.Resume(id); await Verify(await Wait(m, id), TestServer.SlowSize);
    }
});
await Check("process kill recovers atomic checkpoint", async () =>
{
    var p = Paths("process-crash"); string marker = Path.Combine(p.output, "ready.txt");
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(typeof(TestServer).Assembly.Location);
    foreach (string arg in new[] { "--crash-child", p.state, p.output, server.Url("slow-crash"), marker }) start.ArgumentList.Add(arg);
    using var child = Process.Start(start) ?? throw new Exception("Child process not started");
    try
    {
        var clock = Stopwatch.StartNew();
        while (!File.Exists(marker) && !child.HasExited && clock.Elapsed.TotalSeconds < 10) await Task.Delay(25);
        Assert(File.Exists(marker), "Child checkpoint marker missing");
        child.Kill(); await child.WaitForExitAsync();
        string id = await File.ReadAllTextAsync(marker);
        await using var m = new DownloadManager(p.state);
        Assert(m.GetSnapshots().Single().State == DownloadState.Paused, "Crash job not paused");
        m.Resume(id); await Verify(await Wait(m, id), TestServer.SlowSize);
    }
    finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
});
await Check("partial length mismatch restarts safely", async () =>
{
    var p = Paths("partial-length"); string id;
    await using (var m = new DownloadManager(p.state))
    { id = m.Add(server.Url("slow-length"), p.output, "length.bin"); await Wait(m, id, s => s.Received > 256 * 1024); }
    var part = Directory.GetFiles(Path.Combine(p.output, ".streamharbor-" + id), "*.part")[0];
    await File.AppendAllTextAsync(part, "junk");
    await using var resumed = new DownloadManager(p.state); resumed.Resume(id);
    await Verify(await Wait(resumed, id), TestServer.SlowSize);
});
await Check("missing validator restarts rather than append", async () =>
{
    var p = Paths("novalidator"); await using var m = new DownloadManager(p.state);
    string id = m.Add(server.Url("slow-novalidator"), p.output, "none.bin");
    await Wait(m, id, s => s.Received > 256 * 1024); m.Pause(id); await Task.Delay(200); m.Resume(id);
    await Verify(await Wait(m, id), TestServer.SlowSize);
    Assert(server.Requests.Where(r => r.Path == "slow-novalidator").All(r => r.From is null or 0), "Unvalidated ranged resume");
});
await Check("validator changed across pause", async () =>
{
    var p = Paths("pause-change"); await using var m = new DownloadManager(p.state);
    string id = m.Add(server.Url("slow-pausechange"), p.output, "change.bin");
    await Wait(m, id, s => s.Received > 256 * 1024); m.Pause(id); await Task.Delay(200);
    server.Changed = true; m.Resume(id); await Verify(await Wait(m, id), TestServer.SlowSize, 2);
});
await Check("immediate pause resume race", async () =>
{
    var p = Paths("pause-race"); await using var m = new DownloadManager(p.state);
    string id = m.Add(server.Url("slow-race"), p.output, "race.bin");
    await Wait(m, id, s => s.Received > 0); m.Pause(id); m.Resume(id);
    await Verify(await Wait(m, id), TestServer.SlowSize);
});
await Check("idle read timeout bounded", async () =>
{
    var p = Paths("stall"); await using var m = new DownloadManager(p.state);
    m.Settings.MaxRetries = 0; m.SaveSettings(); var clock = Stopwatch.StartNew();
    var s = await Wait(m, m.Add(server.Url("stall"), p.output, "stall.bin"), seconds: 25);
    Assert(s.State == DownloadState.Failed && clock.Elapsed.TotalSeconds is >= 14 and < 24, "Idle timeout not bounded");
});
await Check("cancel stalled body promptly", async () =>
{
    var p = Paths("stall-cancel"); var m = new DownloadManager(p.state);
    var id = m.Add(server.Url("stall-cancel"), p.output, "cancel.bin");
    await Wait(m, id, s => s.Received > 0); var clock = Stopwatch.StartNew(); await m.DisposeAsync();
    Assert(clock.Elapsed.TotalSeconds < 2, "Cancellation stalled");
});
await Check("redirect loop bounded", async () =>
{
    var p = Paths("redirect-loop"); await using var m = new DownloadManager(p.state); m.Settings.MaxRetries = 0; m.SaveSettings();
    var s = await Wait(m, m.Add(server.Url("redirect-loop"), p.output, "loop.bin"));
    Assert(s.State == DownloadState.Failed && server.Requests.Count(r => r.Path == "redirect-loop") <= 9, "Redirects unbounded");
});
await Check("encoded range response rejected", async () =>
{
    var p = Paths("encoded"); await using var m = new DownloadManager(p.state); m.Settings.MaxRetries = 0; m.SaveSettings();
    var s = await Wait(m, m.Add(server.Url("encoded"), p.output, "encoded.bin"));
    Assert(s.State == DownloadState.Failed && !File.Exists(s.Destination), "Encoded bytes published");
});
await Check("queue order concurrency schedule", async () =>
{
    var p = Paths("queue"); await using var m = new DownloadManager(p.state);
    m.Settings.MaxConcurrent = 1; m.SaveSettings();
    string blocker = m.Add(server.Url("slow-queue"), p.output, "block.bin");
    await Wait(m, blocker, s => s.Received > 0);
    string a = m.Add(server.Url("queue-a"), p.output, "a.bin");
    string b = m.Add(server.Url("queue-b"), p.output, "b.bin");
    m.Move(b, -1); Assert(m.GetSnapshots()[1].Id == b, "Move failed");
    await Verify(await Wait(m, blocker), TestServer.SlowSize);
    await Verify(await Wait(m, b), TestServer.NormalSize); await Verify(await Wait(m, a), TestServer.NormalSize);
    Assert(server.Requests.FindIndex(r => r.Path == "queue-b") < server.Requests.FindIndex(r => r.Path == "queue-a"), "Queue order ignored");
    string scheduled = m.Add(server.Url("scheduled"), p.output, "scheduled.bin", DateTimeOffset.UtcNow.AddMilliseconds(700));
    Assert(m.GetSnapshots().Single(s => s.Id == scheduled).State == DownloadState.Scheduled, "Not scheduled");
    await Task.Delay(200); Assert(!server.Requests.Any(r => r.Path == "scheduled"), "Schedule early");
    await Verify(await Wait(m, scheduled), TestServer.NormalSize);
});
await Check("parallel jobs", async () =>
{
    var p = Paths("concurrent"); await using var m = new DownloadManager(p.state);
    string a = m.Add(server.Url("slow-concurrent-a"), p.output, "a.bin");
    string b = m.Add(server.Url("slow-concurrent-b"), p.output, "b.bin");
    await Wait(m, a, s => s.Received > 0); await Wait(m, b, s => s.Received > 0);
    Assert(m.GetSnapshots().Count(s => s.State == DownloadState.Downloading) == 2, "No concurrent jobs");
    await Task.WhenAll(Verify(await Wait(m, a), TestServer.SlowSize), Verify(await Wait(m, b), TestServer.SlowSize));
});
await Check("remove active safe; completed file preserved", async () =>
{
    var p = Paths("remove"); await using var m = new DownloadManager(p.state);
    string active = m.Add(server.Url("slow-remove"), p.output, "active.bin");
    await Wait(m, active, s => s.Received > 0); m.Remove(active); await Task.Delay(300);
    Assert(m.GetSnapshots().Count == 0 && !Directory.Exists(Path.Combine(p.output, ".streamharbor-" + active)), "Active partial not cleaned");
    string completed = m.Add(server.Url("range-remove"), p.output, "completed.bin");
    var done = await Wait(m, completed); await Verify(done, TestServer.NormalSize); m.Remove(completed);
    Assert(File.Exists(done.Destination), "Remove deleted completed file");
});
await Check("filename URL settings validation", async () =>
{
    var p = Paths("validation"); await using var m = new DownloadManager(p.state);
    foreach (string name in new[] { "../escape", "..\\escape", "CON.txt", "LPT1", "COM¹.bin", "x:stream", "trailing.", "bad?file", "NUL", "con .txt" })
    {
        bool rejected = false; try { m.Add(server.Url("range"), p.output, name); } catch (ArgumentException) { rejected = true; }
        Assert(rejected, "Accepted unsafe name " + name);
    }
    foreach (string url in new[] { "file:///test", "https://user:secret@localhost/file", "relative", "ftp://localhost/a" })
    { bool rejected = false; try { m.Add(url, p.output); } catch (ArgumentException) { rejected = true; } Assert(rejected, "Accepted unsafe URL"); }
    m.Settings.Segments = 17; bool invalid = false; try { m.SaveSettings(); } catch (ArgumentException) { invalid = true; }
    Assert(invalid, "Invalid settings persisted"); m.Settings.Segments = 4;
});
await Check("destination collisions never overwrite", async () =>
{
    var p = Paths("collision"); await using var m = new DownloadManager(p.state);
    await File.WriteAllTextAsync(Path.Combine(p.output, "exists.bin"), "preserve");
    bool rejected = false; try { m.Add(server.Url("range"), p.output, "exists.bin"); } catch (IOException) { rejected = true; }
    Assert(rejected, "Existing file accepted");
    string id = m.Add(server.Url("slow-collision"), p.output, "race.bin"); await Wait(m, id, s => s.Received > 0);
    await File.WriteAllTextAsync(Path.Combine(p.output, "race.bin"), "preserve-race");
    Assert((await Wait(m, id)).State == DownloadState.Failed, "Late collision did not fail");
    Assert(await File.ReadAllTextAsync(Path.Combine(p.output, "race.bin")) == "preserve-race", "Overwritten existing file");
});
await Check("settings persistence and data lock", async () =>
{
    var p = Paths("settings");
    await using (var m = new DownloadManager(p.state))
    {
        m.Settings.DownloadDirectory = p.output; m.Settings.MaxConcurrent = 3; m.Settings.Segments = 7; m.Settings.MaxRetries = 1; m.SaveSettings();
        bool locked = false; try { await using var second = new DownloadManager(p.state); } catch (IOException) { locked = true; }
        Assert(locked, "Second manager acquired data directory");
    }
    await using var restored = new DownloadManager(p.state);
    Assert(restored.Settings.MaxConcurrent == 3 && restored.Settings.Segments == 7 && restored.Settings.MaxRetries == 1 && restored.Settings.DownloadDirectory == p.output, "Settings lost");
});
await Check("corrupt state fails without overwrite", async () =>
{
    var p = Paths("corrupt"); Directory.CreateDirectory(p.state); string file = Path.Combine(p.state, "state.json");
    await File.WriteAllTextAsync(file, "{broken"); bool rejected = false;
    try { await using var m = new DownloadManager(p.state); } catch (System.Text.Json.JsonException) { rejected = true; }
    Assert(rejected && await File.ReadAllTextAsync(file) == "{broken", "Corrupt state overwritten");
});
await Check("incomplete metadata fails without overwrite", async () =>
{
    var p = Paths("incomplete"); Directory.CreateDirectory(p.state); string file = Path.Combine(p.state, "state.json");
    await File.WriteAllTextAsync(file, "{}"); bool rejected = false;
    try { await using var m = new DownloadManager(p.state); } catch (System.Text.Json.JsonException) { rejected = true; }
    Assert(rejected && await File.ReadAllTextAsync(file) == "{}", "Incomplete state replaced");
});
await Check("unsaved invalid settings cannot poison persistence", async () =>
{
    var p = Paths("invalid-unsaved");
    await using (var m = new DownloadManager(p.state))
    { m.Settings.Segments = -1; await Verify(await Wait(m, m.Add(server.Url("range-invalid"), p.output, "valid.bin")), TestServer.NormalSize); }
    await using var restored = new DownloadManager(p.state);
    Assert(restored.Settings.Segments == 4, "Unsaved settings persisted");
});
await Check("structured logs exclude secrets and URL", async () =>
{
    var p = Paths("logs");
    await using (var m = new DownloadManager(p.state))
    { var id = m.Add(server.Url("range-log") + "?token=TOP_SECRET", p.output, "log.bin"); await Verify(await Wait(m, id), TestServer.NormalSize); }
    var logs = await File.ReadAllTextAsync(Path.Combine(p.state, "events.jsonl"));
    Assert(!logs.Contains("TOP_SECRET") && !logs.Contains("http") && !logs.Contains("token"), "Sensitive log data");
    foreach (var line in logs.Split('\n', StringSplitOptions.RemoveEmptyEntries)) using (System.Text.Json.JsonDocument.Parse(line)) { }
});

string report = $"# Harbor production-engine checks\n\nExecuted: {DateTimeOffset.UtcNow:O}\n\nRuntime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}\n\nCommand: `dotnet run --project app/Harbor.Checks/Harbor.Checks.csproj -c Release`\n\n" +
    string.Join("\n", results.Select(r => "- " + r)) + $"\n\nTotal: {results.Count - failed}/{results.Count} passed.\n\nArtifacts: `{root}`\n\nLimitations: local HTTP only; no public TLS/proxy integration, power-loss durability simulation, GUI, or browser checks. Real child-process termination exercised; power-loss persistence not claimed. Memory bounded by fixed 64 KiB transfer buffers per segment plus fixed copy buffers; 64 MiB file hashed without whole-file allocation. Destination volume needs space for partials plus assembled file before atomic publication. External mutation of private partials by same-user software is outside trust boundary; length checked, not cryptographically authenticated.\n";
var reportPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "RESULTS.md");
await File.WriteAllTextAsync(Path.GetFullPath(reportPath), report);
Console.WriteLine($"RESULT {results.Count - failed}/{results.Count} passed; artifacts {root}");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed record RequestInfo(string Path, long? From, long? To);
sealed class TestServer : IAsyncDisposable
{
    public const long NormalSize = 2 * 1024 * 1024 + 137;
    public const long SlowSize = 8 * 1024 * 1024 + 4;
    public const long LargeSize = 64 * 1024 * 1024 + 17;
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;
    private readonly ConcurrentBag<Task> workers = [];
    private readonly ConcurrentDictionary<string, int> counts = new();
    private readonly List<RequestInfo> requests = [];
    public bool Changed { get; set; }
    public List<RequestInfo> Requests { get { lock (requests) return requests.ToList(); } }
    public TestServer() { listener.Start(); loop = Accept(); }
    public string Url(string path) => $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/{path}";
    public static void Fill(Span<byte> buffer, long offset, int version)
    { for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)(((offset + i) * 31 + ((offset + i) >> 8) * 17 + version * 53) & 255); }
    private async Task Accept()
    {
        try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); workers.Add(Handle(client)); } }
        catch (OperationCanceledException) { }
    }
    private async Task Handle(TcpClient client)
    {
        using (client)
        try
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
            string? first = await reader.ReadLineAsync(stop.Token); if (first is null) return;
            string path = first.Split(' ')[1].Split('?')[0].TrimStart('/');
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 } line)
            { int colon = line.IndexOf(':'); if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim(); }
            long? from = null, to = null;
            if (headers.TryGetValue("Range", out string? range))
            { var values = range[6..].Split('-'); from = long.Parse(values[0]); to = long.Parse(values[1]); }
            lock (requests) requests.Add(new RequestInfo(path, from, to));
            int count = counts.AddOrUpdate(path, 1, (_, value) => value + 1);
            if (path == "redirect") { await Head(302, "Location: " + Url("range-redirect") + "\r\nContent-Length: 0\r\n"); return; }
            if (path == "redirect-loop") { await Head(302, "Location: " + Url("redirect-loop") + "\r\nContent-Length: 0\r\n"); return; }
            if (path == "416" && from is not null && !(from == 0 && to == 0))
            { await Head(416, $"Content-Range: bytes */{NormalSize}\r\nContent-Length: 0\r\n"); return; }
            if (path == "404" || path == "503" || path == "recover" && count < 3)
            { await Head(path == "404" ? 404 : 503, "Content-Length: 0\r\n"); return; }
            long size = path == "large" ? LargeSize : path.StartsWith("slow") ? SlowSize : path == "empty" ? 0 : NormalSize;
            bool probe = from == 0 && to == 0;
            bool ranged = from is not null && path is not ("sequential" or "ignored" or "false" or "unknown" or "empty" or "stall" or "stall-cancel");
            if (path == "reject" && !probe) ranged = false;
            int version = path == "changed" && !probe || path == "slow-pausechange" && Changed ? 2 : 1;
            string validator = path == "weak" ? "ETag: W/\"v1\"\r\n" : path == "modified" ? "Last-Modified: Mon, 01 Jan 2024 00:00:00 GMT\r\n" : path.Contains("novalidator") ? "" : $"ETag: \"v{version}\"\r\n";
            long start = ranged ? from!.Value : 0, end = ranged ? Math.Min(to!.Value, size - 1) : size - 1;
            long length = end - start + 1;
            string contentRange = ranged ? $"Content-Range: bytes {start}-{end}/{size}\r\n" : "";
            if (path == "malformed") contentRange = "Content-Range: nonsense\r\n";
            if (path == "wrong" && !probe && ranged) contentRange = $"Content-Range: bytes {start + 1}-{end}/{size}\r\n";
            bool chunked = path == "unknown";
            await Head(ranged ? 206 : 200, validator + (path == "encoded" ? "Content-Encoding: gzip\r\n" : "") + "Accept-Ranges: bytes\r\n" + contentRange + (chunked ? "Transfer-Encoding: chunked\r\n" : $"Content-Length: {length}\r\n"));
            bool drop = path == "drop" && !probe && count <= 2;
            long sent = 0; byte[] buffer = new byte[32768];
            while (sent < length)
            {
                int bytes = (int)Math.Min(buffer.Length, length - sent); Fill(buffer.AsSpan(0, bytes), start + sent, version);
                if (chunked) await stream.WriteAsync(Encoding.ASCII.GetBytes($"{bytes:X}\r\n"), stop.Token);
                await stream.WriteAsync(buffer.AsMemory(0, bytes), stop.Token);
                if (chunked) await stream.WriteAsync("\r\n"u8.ToArray(), stop.Token);
                sent += bytes;
                if (path.StartsWith("stall")) { await Task.Delay(30000, stop.Token); return; }
                if (drop && sent >= 131072) return;
                if (path.StartsWith("slow") && !probe) await Task.Delay(path == "slow-crash" ? 40 : 8, stop.Token);
            }
            if (chunked) await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), stop.Token);
            async Task Head(int status, string extra) => await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nConnection: close\r\n{extra}\r\n"), stop.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
    }
    public async ValueTask DisposeAsync()
    { stop.Cancel(); listener.Stop(); await loop; await Task.WhenAll(workers); stop.Dispose(); }
}
