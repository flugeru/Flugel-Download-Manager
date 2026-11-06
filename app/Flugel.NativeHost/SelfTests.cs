using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Flugel.NativeHost;

internal static class SelfTests
{
    internal static int Run()
    {
        var count = 0;
        void Check(string name, Action test)
        {
            test();
            count++;
            Console.WriteLine($"PASS {name}");
        }

        static void Assert(bool value)
        {
            if (!value) throw new InvalidOperationException("Assertion failed.");
        }

        static void Reject(Action test)
        {
            try { test(); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Invalid input was accepted.");
        }

        static byte[] Request(string url) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1, action = "enqueue", url
        });

        try
        {
            Check("valid HTTP(S), Unicode and escaped query", () =>
            {
                foreach (var url in new[] { "https://example.test/file?q=%22&x=1", "http://localhost:8080/", "https://example.test/日本語", "https://[::1]/" })
                    Assert(Program.ParseRequest(Request(url)) == url);
            });
            Check("reject non-HTTP, credentials, ambiguous URL and controls", () =>
            {
                foreach (var url in new[] { "file:///C:/x", "javascript:alert(1)", "ftp://example.test/x", "https://u:p@example.test/", "https://@example.test/", "https://u%40p@example.test/", "https://", "//example.test/x", " https://example.test/", "https://example.test/a b", "https://example.test/\n", "https:\\example.test/x", "https://example.test/\\x", "https://example.test/\u0000" })
                    Reject(() => Program.ParseRequest(Request(url)));
            });
            Check("reject malformed schema, duplicates and JSON", () =>
            {
                foreach (var json in new[] { "null", "[]", "{}", "{", "{\"version\":1,\"action\":\"enqueue\",\"url\":\"https://example.test/\",\"extra\":0}", "{\"version\":1,\"version\":1,\"action\":\"enqueue\",\"url\":\"https://example.test/\"}", "{\"version\":2,\"action\":\"enqueue\",\"url\":\"https://example.test/\"}", "{\"version\":\"1\",\"action\":\"enqueue\",\"url\":\"https://example.test/\"}", "{\"version\":1,\"action\":\"run\",\"url\":\"https://example.test/\"}", "{\"version\":1,\"action\":\"enqueue\",\"url\":12}", "{\"version\":1,\"action\":\"enqueue\",\"url\":\"https://example.test/\",}", "{} {}" })
                    Reject(() => Program.ParseRequest(Encoding.UTF8.GetBytes(json)));
            });
            Check("reject invalid UTF-8 and oversized parser input", () =>
            {
                Reject(() => Program.ParseRequest([0xff]));
                Reject(() => Program.ParseRequest(Encoding.UTF8.GetBytes("{\"version\":1,\"action\":\"enqueue\",\"url\":\"https://example.test/\\uD800\"}")));
                Reject(() => Program.ParseRequest(new byte[Program.MaxMessageBytes + 1]));
            });
            Check("clean EOF and one-byte reads", () =>
            {
                Assert(Program.ReadFrame(new MemoryStream()) is null);
                var body = Request("https://example.test/");
                Assert(Program.ReadFrame(new ShortReadStream(Frame(body)))!.SequenceEqual(body));
            });
            Check("reject partial header and body", () =>
            {
                foreach (var bytes in new byte[][] { [1], [1, 0], [1, 0, 0], [2, 0, 0, 0, 123] })
                    Reject(() => Program.ReadFrame(new ShortReadStream(bytes)));
            });
            Check("reject zero, 65537 and uint-max lengths", () =>
            {
                foreach (var size in new uint[] { 0, 65537, uint.MaxValue })
                {
                    var header = new byte[4];
                    BinaryPrimitives.WriteUInt32LittleEndian(header, size);
                    Reject(() => Program.ReadFrame(new MemoryStream(header)));
                }
            });
            Check("accept exact 65536-byte frame and valid JSON", () =>
            {
                var url = "https://example.test/";
                var overhead = Request(url).Length;
                var body = Request(url + new string('x', Program.MaxMessageBytes - overhead));
                Assert(body.Length == Program.MaxMessageBytes);
                Assert(Program.ParseRequest(Program.ReadFrame(new ShortReadStream(Frame(body)))!).Length > 65000);
            });
            Check("safe argument list and isolated child stdout", () =>
            {
                const string url = "https://example.test/?q=%22&command=calc.exe";
                var info = Program.DesktopStartInfo(@"C:\Flugel Download Manager Space\Flugel.Desktop\FlugelDownloadManager.exe", url);
                Assert(!info.UseShellExecute && info.Arguments == "");
                Assert(info.ArgumentList.SequenceEqual(new[] { "--url", url }));
                Assert(info.RedirectStandardOutput && info.RedirectStandardError && info.RedirectStandardInput);
            });
            Check("multiple frames, confirmation-only reply, no URL echo", () =>
            {
                using var input = new MemoryStream();
                input.Write(Frame(Request("https://example.test/?secret=one")));
                input.Write(Frame(Request("http://example.test/two")));
                input.Position = 0;
                using var output = new MemoryStream();
                var launches = 0;
                Assert(Program.Run(input, output, _ => { launches++; return true; }) == 0 && launches == 2);
                output.Position = 0;
                for (var i = 0; i < 2; i++)
                {
                    using var reply = JsonDocument.Parse(Program.ReadFrame(output)!);
                    Assert(reply.RootElement.GetProperty("status").GetString() == "accepted-for-confirmation");
                    Assert(!reply.RootElement.TryGetProperty("url", out _));
                }
                Assert(output.Position == output.Length);
            });
            Check("invalid requests never launch", () =>
            {
                using var input = new MemoryStream(Frame(Request("file:///x")));
                using var output = new MemoryStream();
                Assert(Program.Run(input, output, _ => throw new Exception("Must not launch")) == 1);
                output.Position = 0;
                using var reply = JsonDocument.Parse(Program.ReadFrame(output)!);
                Assert(reply.RootElement.GetProperty("error").GetString() == "invalid-request");
            });
            Check("partial frame rejected with framed reply", () =>
            {
                using var output = new MemoryStream();
                Assert(Program.Run(new MemoryStream([1, 0]), output, _ => throw new Exception("Must not launch")) == 1);
                output.Position = 0;
                using var reply = JsonDocument.Parse(Program.ReadFrame(output)!);
                Assert(reply.RootElement.GetProperty("error").GetString() == "invalid-frame");
            });
            Check("desktop unavailable is rejection", () =>
            {
                using var output = new MemoryStream();
                Assert(Program.Run(new MemoryStream(Frame(Request("https://example.test/"))), output, _ => false) == 0);
                output.Position = 0;
                using var reply = JsonDocument.Parse(Program.ReadFrame(output)!);
                Assert(reply.RootElement.GetProperty("error").GetString() == "desktop-unavailable");
            });
            Check("enqueue-media and ping structured messages", () =>
            {
                var mediaMsg = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1,
                    action = "enqueue-media",
                    url = "https://cdn.example.test/video.m3u8",
                    fileName = "lecture-1080p.ts",
                    referrer = "https://portal.example.test/watch",
                    pageTitle = "Lecture 1",
                    mediaQuality = "1080p HLS",
                    segmentUrls = new[] { "https://cdn.example.test/seg1.ts", "https://cdn.example.test/seg2.ts" }
                });
                var parsed = Program.ParseStructuredRequest(mediaMsg);
                Assert(parsed.Action == "enqueue-media" && parsed.FileName == "lecture-1080p.ts" && parsed.SegmentUrls?.Count == 2);

                var pingMsg = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, action = "ping" });
                using var outPing = new MemoryStream();
                Assert(Program.RunWithPayload(new MemoryStream(Frame(pingMsg)), outPing, _ => throw new Exception("Ping must not launch")) == 0);
                outPing.Position = 0;
                using var pingReply = JsonDocument.Parse(Program.ReadFrame(outPing)!);
                Assert(pingReply.RootElement.GetProperty("status").GetString() == "ready");
            });
            Check("direct MP4 from browser with null optional metadata", () =>
            {
                var body = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1, action = "enqueue-media", url = "https://cdn.example.test/588340.mp4",
                    fileName = "588340.mp4", referrer = "https://portal.example.test/watch",
                    pageTitle = "Episode", mediaQuality = "1080p", segmentUrls = (string[]?)null
                });
                var request = Program.ParseStructuredRequest(body);
                Assert(request.MediaQuality == "1080p" && request.SegmentUrls is null);
                using var output = new MemoryStream();
                Assert(Program.RunWithPayload(new MemoryStream(Frame(body)), output, req => req == request) == 0);
                output.Position = 0;
                using var reply = JsonDocument.Parse(Program.ReadFrame(output)!);
                Assert(reply.RootElement.GetProperty("status").GetString() == "accepted-for-confirmation");
                Reject(() => Program.ParseStructuredRequest(Encoding.UTF8.GetBytes(
                    "{\"version\":1,\"action\":\"enqueue-media\",\"url\":\"https://example.test/video.mp4\",\"segmentUrls\":42}")));
            });
            Check("busy desktop preserves rejection reason", () =>
            {
                using var output = new MemoryStream();
                Assert(Program.Run(new MemoryStream(Frame(Request("https://example.test/video.mp4"))), output,
                    _ => throw new Program.DesktopRejection("desktop-busy")) == 0);
                output.Position = 0;
                using var reply = JsonDocument.Parse(Program.ReadFrame(output)!);
                Assert(reply.RootElement.GetProperty("error").GetString() == "desktop-busy");
            });
            Check("session cookie accepted only on enqueue-media and rejected when unsafe", () =>
            {
                var ok = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1, action = "enqueue-media", url = "https://nuvem.example.test/a.mkv", cookie = "session=abc; lang=pt"
                });
                Assert(Program.ParseStructuredRequest(ok).CookieHeader == "session=abc; lang=pt");
                Reject(() => Program.ParseStructuredRequest(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1, action = "enqueue-media", url = "https://nuvem.example.test/a.mkv", cookie = "a=1\r\nX-Injected: 1"
                })));
                Reject(() => Program.ParseStructuredRequest(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1, action = "enqueue", url = "https://nuvem.example.test/a.mkv", cookie = "a=1"
                })));
                Reject(() => Program.ParseStructuredRequest(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1, action = "enqueue-media", url = "https://nuvem.example.test/a.mkv", cookie = "a=" + new string('x', 8200)
                })));
                Reject(() => Program.ParseStructuredRequest(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1, action = "enqueue-media", url = "https://nuvem.example.test/a.mkv", cookie = "novalue"
                })));
            });
            Console.WriteLine($"PASS {count} self-tests; no desktop launched.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL after {count} checks: {error.Message}");
            return 1;
        }
    }

    private static byte[] Frame(byte[] body)
    {
        var bytes = new byte[body.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)body.Length);
        body.CopyTo(bytes, 4);
        return bytes;
    }

    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]);
    }
}
