using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Harbor.NativeHost;

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

        // Chromium supplies origin/window arguments. Never forward these to desktop.
        try
        {
            return Run(Console.OpenStandardInput(), Console.OpenStandardOutput(),
                url => LaunchDesktop(url, testDirectory));
        }
        catch (IOException)
        {
            Console.Error.WriteLine("Native messaging pipe unavailable.");
            return 1;
        }
    }

    internal static int Run(Stream input, Stream output, Func<string, bool> launch)
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

            string url;
            try
            {
                url = ParseRequest(message);
            }
            catch (InvalidDataException)
            {
                Reply(output, "rejected", "invalid-request");
                return 1;
            }

            if (!launch(url))
            {
                Reply(output, "rejected", "desktop-unavailable");
                continue;
            }

            Reply(output, "accepted-for-confirmation");
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

    internal static string ParseRequest(byte[] message)
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
            string? url = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    throw new InvalidDataException("Duplicate field.");
                switch (property.Name)
                {
                    case "version" when property.Value.ValueKind == JsonValueKind.Number &&
                                        property.Value.TryGetInt32(out var version) && version == 1:
                        break;
                    case "action" when property.Value.ValueKind == JsonValueKind.String &&
                                       property.Value.GetString() == "enqueue":
                        break;
                    case "url" when property.Value.ValueKind == JsonValueKind.String:
                        url = property.Value.GetString();
                        break;
                    default:
                        throw new InvalidDataException("Unsupported field or value.");
                }
            }

            if (seen.Count != 3 || url is null || !ValidUrl(url))
                throw new InvalidDataException("Invalid URL or missing field.");
            return url;
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or
                                      ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Invalid JSON or UTF-8.", error);
        }
    }

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

    internal static ProcessStartInfo DesktopStartInfo(string executable, string url)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            // Child must never inherit native-messaging stdout.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("--url");
        info.ArgumentList.Add(url);
        return info;
    }

    private static bool LaunchDesktop(string url, string? testDirectory = null)
    {
        var executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "Harbor.Desktop", "StreamHarbor.exe"));
        if (!File.Exists(executable))
            return false;
        try
        {
            // Redirects replace child stdio but CreateProcess still inherits other inheritable
            // handles. Keep original browser/Node pipes out of the long-lived GUI process.
            if (OperatingSystem.IsWindows())
            {
                foreach (var id in new[] { -10, -11, -12 })
                {
                    var handle = GetStdHandle(id);
                    if (handle != IntPtr.Zero && handle != new IntPtr(-1) &&
                        !SetHandleInformation(handle, 1, 0)) // HANDLE_FLAG_INHERIT
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            var start = DesktopStartInfo(executable, url);
            if (testDirectory is not null)
            {
                start.ArgumentList.Add("--data-dir");
                start.ArgumentList.Add(testDirectory);
            }
            using var process = Process.Start(start);
            if (process is null)
                return false;
            // Desktop is a GUI process. Close redirected pipes instead of waiting for GUI exit.
            process.StandardInput.Close();
            process.StandardOutput.Close();
            process.StandardError.Close();
            return true;
        }
        catch (Exception error) when (error is Win32Exception or IOException or
                                      UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int id);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    private static void Reply(Stream output, string status, string? error = null)
    {
        var message = error is null
            ? JsonSerializer.SerializeToUtf8Bytes(new { version = 1, status })
            : JsonSerializer.SerializeToUtf8Bytes(new { version = 1, status, error });
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)message.Length);
        output.Write(header);
        output.Write(message);
        output.Flush();
    }
}
