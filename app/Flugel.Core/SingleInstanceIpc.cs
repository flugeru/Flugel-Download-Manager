using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Flugel.Core;

public sealed class IpcMediaVariantDto
{
    public string Url { get; set; } = "";
    public string Label { get; set; } = "";
    public string Format { get; set; } = "";
    public string? FileName { get; set; }
    public long? SizeBytes { get; set; }
    public bool IsProtected { get; set; }
    public List<string>? SegmentUrls { get; set; }
}

public sealed class IpcRequestMessage
{
    public int Version { get; set; } = 1;
    public string Action { get; set; } = "enqueue"; // enqueue, activate, query-config
    public string? Url { get; set; }
    public string? FileName { get; set; }
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public string? CookieHeader { get; set; }
    public string? PageTitle { get; set; }
    public string? Category { get; set; }
    public string? QueueName { get; set; }
    public string? MediaQuality { get; set; }
    public bool StartImmediately { get; set; }
    public List<string>? SegmentUrls { get; set; }
    public List<IpcMediaVariantDto>? MediaVariants { get; set; }
}

public sealed class IpcResponseMessage
{
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "accepted-for-confirmation";
    public bool ForwardedToRunningInstance { get; set; } = true;
    public string? DownloadId { get; set; }
    public bool BrowserAutoIntercept { get; set; } = true;
    public bool BrowserVideoBalloonEnabled { get; set; } = true;
    public string AutoCaptureExtensions { get; set; } = "";
    public string ExcludedHosts { get; set; } = "";
}

public sealed class SingleInstanceIpcServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly Func<IpcRequestMessage, Task<IpcResponseMessage>> _handler;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public SingleInstanceIpcServer(string dataDirectory, Func<IpcRequestMessage, Task<IpcResponseMessage>> handler)
    {
        _pipeName = SingleInstanceIpc.GetPipeName(dataDirectory);
        _handler = handler;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                var connected = server;
                server = null;
                _ = Task.Run(() => HandleClientAsync(connected));
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                server?.Dispose();
                break;
            }
            catch
            {
                server?.Dispose();
                try { await Task.Delay(100, _cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                string? line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line) || line.Length > 65536) return;

                var req = JsonSerializer.Deserialize<IpcRequestMessage>(line);
                if (req is null) return;

                var resp = await _handler(req).ConfigureAwait(false);
                string json = JsonSerializer.Serialize(resp) + "\n";
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await pipe.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                // Ignore malformed client frames.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try { await _acceptLoop.ConfigureAwait(false); } catch { }
        _cts.Dispose();
    }
}

public static class SingleInstanceIpc
{
    public static string GetPipeName(string dataDirectory)
    {
        string normalized = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        string user = Environment.UserName.ToUpperInvariant();
        if (OperatingSystem.IsWindows())
        {
            try { user = WindowsIdentity.GetCurrent().User?.Value ?? user; } catch { }
        }
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(user + "|" + normalized));
        return "FlugelDownloadManager.IPC." + Convert.ToHexString(hash)[..24];
    }

    public static async Task<IpcResponseMessage?> TrySendAsync(string dataDirectory, IpcRequestMessage request, int timeoutMs = 2500)
    {
        string pipeName = GetPipeName(dataDirectory);
        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeoutMs).ConfigureAwait(false);
            string payload = JsonSerializer.Serialize(request) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
            await client.FlushAsync(cts.Token).ConfigureAwait(false);

            using var reader = new StreamReader(client, Encoding.UTF8, false, 4096, leaveOpen: true);
            string? reply = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(reply)) return null;
            return JsonSerializer.Deserialize<IpcResponseMessage>(reply);
        }
        catch
        {
            return null;
        }
    }
}
