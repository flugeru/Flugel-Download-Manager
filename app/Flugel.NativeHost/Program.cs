using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Flugel.NativeHost;

internal sealed record ParsedHostRequest(
    string Action,
    string Url,
    string? FileName = null,
    string? Referrer = null,
    string? PageTitle = null,
    string? MediaQuality = null,
    IReadOnlyList<string>? SegmentUrls = null,
    string? CookieHeader = null,
    string? UserAgent = null,
    string? RequestId = null);

internal static class Program
{
    internal const int MaxMessageBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static int Main(string[] args)
    {
        if (args is ["--self-test"])
            return SelfTests.Run();

        string? testDirectory = null;
        if (args is ["--self-test-desktop", var directory])
        {
            testDirectory = Path.GetFullPath(directory);
            if (Directory.Exists(testDirectory) || File.Exists(testDirectory))
                return 2;
        }

        try
        {
            return RunWithPayload(Console.OpenStandardInput(), Console.OpenStandardOutput(),
                req => ForwardOrLaunchDesktop(req, testDirectory));
        }
        catch (IOException)
        {
            Console.Error.WriteLine("Native messaging pipe unavailable.");
            return 1;
        }
    }

    // Legacy convenience used by older checks: a launch result maps to the neutral "launching" status.
    internal static int Run(Stream input, Stream output, Func<string, bool> launch) =>
        RunWithPayload(input, output, req => launch(req.Url) ? HandoffStatus.Launching : null);

    // Handoff statuses relayed verbatim to the extension. It cancels the paused browser copy
    // only on "confirmed"; "rejected"/"unknown-request" (and deadline expiry) restore the copy.
    internal static class HandoffStatus
    {
        internal const string Launching = "launching";
        internal const string Waiting = "waiting-user-confirmation";
        internal const string Confirmed = "confirmed";
        internal const string RejectedStatus = "rejected";
        internal const string Unknown = "unknown-request";
    }

    internal static int RunWithPayload(Stream input, Stream output, Func<ParsedHostRequest, string?> launch)
    {
        while (true)
        {
            byte[]? message;
            try
            {
                message = ReadFrame(input);
            }
            catch (InvalidDataException)
            {
                Reply(output, "rejected", "invalid-frame");
                return 1;
            }

            if (message is null)
                return 0;

            ParsedHostRequest req;
            try
            {
                req = ParseStructuredRequest(message);
            }
            catch (InvalidDataException)
            {
                Reply(output, "rejected", "invalid-request");
                return 1;
            }

            if (req.Action == "ping")
            {
                Reply(output, "ready");
                continue;
            }

            string? status;
            try { status = launch(req); }
            catch (DesktopRejection error)
            {
                Reply(output, "rejected", error.Code);
                continue;
            }
            if (status is null)
            {
                Reply(output, "rejected", "desktop-unavailable");
                continue;
            }

            Reply(output, status);
        }
    }

    internal static byte[]? ReadFrame(Stream input)
    {
        Span<byte> header = stackalloc byte[4];
        var first = input.Read(header);
        if (first == 0)
            return null;
        ReadRest(input, header[first..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (size is 0 or > MaxMessageBytes)
            throw new InvalidDataException("Invalid frame size.");
        var message = new byte[(int)size];
        ReadRest(input, message);
        return message;
    }

    private static void ReadRest(Stream input, Span<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            var count = input.Read(bytes);
            if (count == 0)
                throw new InvalidDataException("Incomplete frame.");
            bytes = bytes[count..];
        }
    }

    internal static string ParseRequest(byte[] message) => ParseStructuredRequest(message).Url;

    internal static ParsedHostRequest ParseStructuredRequest(byte[] message)
    {
        if (message.Length is 0 or > MaxMessageBytes)
            throw new InvalidDataException("Invalid request size.");
        try
        {
            using var document = JsonDocument.Parse(StrictUtf8.GetString(message),
                new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Expected object.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? action = null;
            string? url = null;
            string? fileName = null;
            string? referrer = null;
            string? pageTitle = null;
            string? mediaQuality = null;
            List<string>? segmentUrls = null;
            string? cookie = null;
            string? userAgent = null;
            string? requestId = null;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    throw new InvalidDataException("Duplicate field.");
                switch (property.Name)
                {
                    case "version" when property.Value.ValueKind == JsonValueKind.Number &&
                                        property.Value.TryGetInt32(out var version) && version == 1:
                        break;
                    case "action" when property.Value.ValueKind == JsonValueKind.String:
                        action = property.Value.GetString();
                        if (action is not ("enqueue" or "enqueue-media" or "ping" or "job-status"))
                            throw new InvalidDataException("Unsupported action.");
                        break;
                    case "requestId" when property.Value.ValueKind == JsonValueKind.String:
                        requestId = property.Value.GetString();
                        if (requestId is null || !ValidRequestId(requestId))
                            throw new InvalidDataException("Invalid request id.");
                        break;
                    case "url" when property.Value.ValueKind == JsonValueKind.String:
                        url = property.Value.GetString();
                        break;
                    case "fileName" when property.Value.ValueKind == JsonValueKind.String:
                        fileName = property.Value.GetString();
                        break;
                    case "referrer" when property.Value.ValueKind == JsonValueKind.String:
                        referrer = property.Value.GetString();
                        break;
                    case "pageTitle" when property.Value.ValueKind == JsonValueKind.String:
                        pageTitle = property.Value.GetString();
                        break;
                    case "mediaQuality" when property.Value.ValueKind == JsonValueKind.String:
                        mediaQuality = property.Value.GetString();
                        break;
                    case "segmentUrls" when property.Value.ValueKind == JsonValueKind.Array:
                        segmentUrls = [];
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } seg || !ValidUrl(seg))
                                throw new InvalidDataException("Invalid segment URL.");
                            segmentUrls.Add(seg);
                        }
                        break;
                    case "cookie" when property.Value.ValueKind == JsonValueKind.String:
                        cookie = property.Value.GetString();
                        break;
                    case "userAgent" when property.Value.ValueKind == JsonValueKind.String:
                        userAgent = property.Value.GetString();
                        break;
                    // Accept older extension payloads with explicitly null optional metadata.
                    case "requestId" or "fileName" or "referrer" or "pageTitle" or "mediaQuality" or "segmentUrls" or "cookie" or "userAgent"
                        when property.Value.ValueKind == JsonValueKind.Null:
                        break;
                    default:
                        throw new InvalidDataException("Unsupported field or value.");
                }
            }

            if (!seen.Contains("version") || action is null)
                throw new InvalidDataException("Missing version or action.");

            if (action == "ping")
            {
                if (seen.Count != 2) throw new InvalidDataException("Extra fields on ping.");
                return new ParsedHostRequest("ping", "https://localhost/");
            }

            if (action == "job-status")
            {
                // Exactly {version, action, requestId}: the extension polls a single handoff.
                if (seen.Count != 3 || requestId is null)
                    throw new InvalidDataException("Invalid job-status fields.");
                return new ParsedHostRequest("job-status", "https://localhost/", RequestId: requestId);
            }

            if (action == "enqueue")
            {
                bool withId = seen.Contains("requestId");
                if (seen.Count != (withId ? 4 : 3) || url is null || !ValidUrl(url))
                    throw new InvalidDataException("Invalid URL or extra/missing field.");
                return new ParsedHostRequest("enqueue", url, RequestId: requestId);
            }

            // enqueue-media allows optional metadata fields (including requestId)
            if (url is null || !ValidUrl(url))
                throw new InvalidDataException("Invalid URL.");
            if (referrer is not null && referrer.Length > 0 && !ValidUrl(referrer))
                throw new InvalidDataException("Invalid referrer URL.");

            if (cookie is not null && !ValidCookieHeader(cookie))
                throw new InvalidDataException("Invalid cookie header.");
            if (userAgent is not null && (userAgent.Length is < 1 or > 2048 || userAgent.Any(char.IsControl)))
                throw new InvalidDataException("Invalid user agent.");

            return new ParsedHostRequest(action, url, fileName, referrer, pageTitle, mediaQuality, segmentUrls, cookie, userAgent, requestId);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or
                                      ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Invalid JSON or UTF-8.", error);
        }
    }

    internal static bool ValidCookieHeader(string value) =>
        value.Length is > 0 and <= 8192 && value.Contains('=') &&
        !value.Any(c => char.IsControl(c));

    // Extension correlation id: UUID-ish, printable, bounded. Not a secret; carried to the desktop
    // via the IPC payload or the temporary pending file so a future poll can resolve the outcome.
    internal static bool ValidRequestId(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static bool ValidUrl(string value)
    {
        if (!(value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
              value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ||
            value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\'))
            return false;
        var authority = value.AsSpan(value.IndexOf("://", StringComparison.Ordinal) + 3);
        var end = authority.IndexOfAny('/', '?', '#');
        if ((end < 0 ? authority : authority[..end]).Contains('@'))
            return false;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.IsWellFormedOriginalString() && !string.IsNullOrEmpty(uri.Host) &&
               uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
    }

    internal static ProcessStartInfo DesktopStartInfo(string executable, string url, ParsedHostRequest? full = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("--url");
        info.ArgumentList.Add(url);
        if (full is not null)
        {
            if (!string.IsNullOrWhiteSpace(full.FileName))
            {
                info.ArgumentList.Add("--filename");
                info.ArgumentList.Add(full.FileName);
            }
            if (!string.IsNullOrWhiteSpace(full.Referrer))
            {
                info.ArgumentList.Add("--referrer");
                info.ArgumentList.Add(full.Referrer);
            }
            if (!string.IsNullOrWhiteSpace(full.MediaQuality))
            {
                info.ArgumentList.Add("--media-quality");
                info.ArgumentList.Add(full.MediaQuality);
            }
        }
        return info;
    }

    internal static string GetIpcPipeName(string dataDirectory)
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

    internal static bool TryForwardToRunningInstance(ParsedHostRequest req, string dataDirectory) =>
        ForwardedStatus(req, dataDirectory) is not null;

    // Relays the desktop's own status (null when no instance could be reached).
    private static string? ForwardedStatus(ParsedHostRequest req, string dataDirectory)
    {
        try
        {
            string pipeName = GetIpcPipeName(dataDirectory);
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect(1200);
            string payload = JsonSerializer.Serialize(new
            {
                Version = 1,
                Action = req.Action,
                req.RequestId,
                req.Url,
                req.FileName,
                req.Referrer,
                req.PageTitle,
                req.MediaQuality,
                req.SegmentUrls,
                req.CookieHeader,
                req.UserAgent
            }) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            using var reader = new StreamReader(client, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            string? line = reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(line)) return null;
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("Status", out var status) && status.GetString() is { } value
                ? value
                : null;
        }
        catch
        {
            return null;
        }
    }

    // Returns the status the extension receives; null means the desktop could not take the request.
    private static string? ForwardOrLaunchDesktop(ParsedHostRequest req, string? testDirectory = null)
    {
        string effectiveDataDir = testDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flugel Download Manager");

        // 1. If a Flugel Download Manager instance is already running for this data directory, forward via same-user Named Pipe IPC!
        var forwarded = ForwardedStatus(req, effectiveDataDir);
        if (forwarded is not null)
        {
            return forwarded switch
            {
                "desktop-busy" or "invalid-request" => throw new DesktopRejection(forwarded),
                // Anything else is relayed verbatim: waiting-user-confirmation, confirmed,
                // rejected, unknown-request, or a legacy accepted-for-confirmation reply.
                _ => forwarded
            };
        }

        // 2. Otherwise launch FlugelDownloadManager.exe
        var executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "Flugel.Desktop", "FlugelDownloadManager.exe"));
        if (!File.Exists(executable))
            return null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (var id in new[] { -10, -11, -12 })
                {
                    var handle = GetStdHandle(id);
                    if (handle != IntPtr.Zero && handle != new IntPtr(-1) &&
                        !SetHandleInformation(handle, 1, 0))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            var start = DesktopStartInfo(executable, req.Url, req);
            bool hasCorrelation = req.RequestId is not null;
            if (req.CookieHeader is not null || req.UserAgent is not null || hasCorrelation)
            {
                // Cookies never go on the command line (visible to other processes); the desktop deletes this file after reading.
                // The correlation id rides in the same file so the desktop can answer "job-status" polls.
                Directory.CreateDirectory(effectiveDataDir);
                string pending = Path.Combine(effectiveDataDir, $"pending-request-{Guid.NewGuid():N}.json");
                File.WriteAllText(pending, JsonSerializer.Serialize(new
                {
                    req.RequestId, req.Url, req.FileName, req.Referrer, req.PageTitle, req.MediaQuality,
                    req.SegmentUrls, req.CookieHeader, req.UserAgent
                }), Encoding.UTF8);
                start.ArgumentList.Add("--pending-request");
                start.ArgumentList.Add(pending);
            }
            if (req.SegmentUrls is { Count: > 0 } && req.CookieHeader is null && req.UserAgent is null)
            {
                Directory.CreateDirectory(effectiveDataDir);
                string segFile = Path.Combine(effectiveDataDir, $"pending-segments-{Guid.NewGuid():N}.json");
                File.WriteAllText(segFile, JsonSerializer.Serialize(req.SegmentUrls), Encoding.UTF8);
                start.ArgumentList.Add("--segments-file");
                start.ArgumentList.Add(segFile);
            }
            if (testDirectory is not null)
            {
                start.ArgumentList.Add("--data-dir");
                start.ArgumentList.Add(testDirectory);
            }
            using var process = Process.Start(start);
            if (process is null)
                return null;
            process.StandardInput.Close();
            process.StandardOutput.Close();
            process.StandardError.Close();
            return HandoffStatus.Launching;
        }
        catch (Exception error) when (error is Win32Exception or IOException or
                                      UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    internal sealed class DesktopRejection(string code) : Exception
    {
        internal string Code { get; } = code;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int id);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    internal static void Reply(Stream output, string status, string? error = null)
    {
        var body = error is null
            ? JsonSerializer.SerializeToUtf8Bytes(new { version = 1, status })
            : JsonSerializer.SerializeToUtf8Bytes(new { version = 1, status, error });
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)body.Length);
        output.Write(header);
        output.Write(body);
        output.Flush();
    }
}
