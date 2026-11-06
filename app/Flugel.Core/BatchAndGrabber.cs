using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Flugel.Core;

public sealed record BatchExpansionPreview(
    string? FirstUrl,
    string? SecondUrl,
    string? LastUrl,
    IReadOnlyList<string> Urls);

public static class BatchUrlExpander
{
    public const int MaxBatchCount = 1000;

    public static BatchExpansionPreview ExpandNumeric(string patternUrl, int from, int to, int width = 1)
    {
        if (string.IsNullOrWhiteSpace(patternUrl) || !patternUrl.Contains('*'))
            throw new ArgumentException("Pattern URL must contain an asterisk (*) placeholder.");
        if (from < 0 || to < from || to - from + 1 > MaxBatchCount)
            throw new ArgumentException($"Numeric range must be valid and produce at most {MaxBatchCount} URLs.");
        if (width is < 1 or > 10)
            throw new ArgumentException("Zero-padding width must be between 1 and 10.");

        var urls = new List<string>(to - from + 1);
        for (int i = from; i <= to; i++)
        {
            string token = i.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
            string expanded = patternUrl.Replace("*", token, StringComparison.Ordinal);
            ValidateExpandedUrl(expanded);
            urls.Add(expanded);
        }
        return BuildPreview(urls);
    }

    public static BatchExpansionPreview ExpandAlphabetic(string patternUrl, char from, char to)
    {
        if (string.IsNullOrWhiteSpace(patternUrl) || !patternUrl.Contains('*'))
            throw new ArgumentException("Pattern URL must contain an asterisk (*) placeholder.");
        bool lower = char.IsAsciiLetterLower(from) && char.IsAsciiLetterLower(to);
        bool upper = char.IsAsciiLetterUpper(from) && char.IsAsciiLetterUpper(to);
        if ((!lower && !upper) || to < from)
            throw new ArgumentException("Letter range must be ASCII a-z or A-Z in ascending order.");

        var urls = new List<string>(to - from + 1);
        for (char c = from; c <= to; c++)
        {
            string expanded = patternUrl.Replace("*", c.ToString(), StringComparison.Ordinal);
            ValidateExpandedUrl(expanded);
            urls.Add(expanded);
        }
        return BuildPreview(urls);
    }

    public static IReadOnlyList<string> ParseUrlList(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return [];
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith('#')) continue;
            ValidateExpandedUrl(rawLine);
            if (seen.Add(rawLine)) list.Add(rawLine);
            if (list.Count >= MaxBatchCount) break;
        }
        return list;
    }

    private static void ValidateExpandedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException($"Invalid expanded HTTP(S) URL: {url}");
    }

    private static BatchExpansionPreview BuildPreview(List<string> urls) =>
        new(
            urls.Count >= 1 ? urls[0] : null,
            urls.Count >= 2 ? urls[1] : null,
            urls.Count >= 1 ? urls[^1] : null,
            urls);
}

public sealed record DiscoveredMediaVariant(
    string Url,
    string ContainerFormat,
    string QualityLabel,
    int? Width,
    int? Height,
    long? Bandwidth,
    string? Codecs,
    bool IsProtected,
    IReadOnlyList<string> SegmentUrls);

public sealed record GrabbedSiteItem(
    string Url,
    string FileName,
    string Category,
    string Kind,
    string Label,
    bool IsProtected = false,
    IReadOnlyList<string>? SegmentUrls = null);

public static class HlsAndDashParser
{
    public static bool IsHlsPlaylistText(string content) =>
        !string.IsNullOrWhiteSpace(content) && content.TrimStart().StartsWith("#EXTM3U", StringComparison.Ordinal);

    public static (bool IsMaster, bool IsProtected, List<DiscoveredMediaVariant> Variants, List<string> DirectSegments) ParseHls(string manifestText, Uri baseUri)
    {
        var variants = new List<DiscoveredMediaVariant>();
        var segments = new List<string>();
        bool isProtected = false;
        bool isMaster = false;

        string[] lines = manifestText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.StartsWith("#EXT-X-KEY:", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("METHOD=NONE", StringComparison.OrdinalIgnoreCase))
            {
                isProtected = true;
            }
            else if (line.StartsWith("#EXT-X-MAP:", StringComparison.OrdinalIgnoreCase))
            {
                var uriMatch = Regex.Match(line, "URI=\"([^\"]+)\"");
                if (uriMatch.Success && Uri.TryCreate(baseUri, uriMatch.Groups[1].Value, out var mapUri) &&
                    (mapUri.Scheme == Uri.UriSchemeHttp || mapUri.Scheme == Uri.UriSchemeHttps))
                {
                    segments.Add(mapUri.AbsoluteUri);
                }
            }
            else if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase))
            {
                isMaster = true;
                int? width = null, height = null;
                long? bandwidth = null;
                string? codecs = null;

                var resMatch = Regex.Match(line, @"RESOLUTION=(\d+)x(\d+)", RegexOptions.IgnoreCase);
                if (resMatch.Success)
                {
                    width = int.Parse(resMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    height = int.Parse(resMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                var bwMatch = Regex.Match(line, @"BANDWIDTH=(\d+)", RegexOptions.IgnoreCase);
                if (bwMatch.Success)
                    bandwidth = long.Parse(bwMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                var codecMatch = Regex.Match(line, "CODECS=\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (codecMatch.Success)
                    codecs = codecMatch.Groups[1].Value;

                while (i + 1 < lines.Length && lines[i + 1].StartsWith('#')) i++;
                if (i + 1 < lines.Length)
                {
                    string nextUri = lines[++i];
                    if (Uri.TryCreate(baseUri, nextUri, out var resolved) &&
                        (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps))
                    {
                        string quality = height is > 0 ? $"{height}p" : bandwidth is > 0 ? $"{bandwidth.Value / 1000} kbps" : "HLS Stream";
                        variants.Add(new DiscoveredMediaVariant(
                            resolved.AbsoluteUri,
                            "HLS (.ts)",
                            quality,
                            width,
                            height,
                            bandwidth,
                            codecs,
                            isProtected,
                            []));
                    }
                }
            }
            else if (!line.StartsWith('#'))
            {
                if (Uri.TryCreate(baseUri, line, out var segUri) &&
                    (segUri.Scheme == Uri.UriSchemeHttp || segUri.Scheme == Uri.UriSchemeHttps))
                {
                    segments.Add(segUri.AbsoluteUri);
                }
            }
        }

        variants.Sort((a, b) => (b.Height ?? 0).CompareTo(a.Height ?? 0));
        return (isMaster, isProtected, variants, segments);
    }

    public static (bool IsProtected, List<DiscoveredMediaVariant> Variants) ParseDashMpd(string mpdXml, Uri baseUri)
    {
        var variants = new List<DiscoveredMediaVariant>();
        bool isProtected = false;
        try
        {
            var doc = XDocument.Parse(mpdXml);
            if (doc.Descendants().Any(e => e.Name.LocalName.Equals("ContentProtection", StringComparison.OrdinalIgnoreCase)))
                isProtected = true;

            string? mpdBase = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "BaseURL")?.Value?.Trim();
            Uri effectiveBase = !string.IsNullOrWhiteSpace(mpdBase) && Uri.TryCreate(baseUri, mpdBase, out var b) ? b : baseUri;

            foreach (var rep in doc.Descendants().Where(e => e.Name.LocalName == "Representation"))
            {
                var adapt = rep.Parent;
                if (rep.Elements().Any(e => e.Name.LocalName == "ContentProtection") ||
                    adapt?.Elements().Any(e => e.Name.LocalName == "ContentProtection") == true)
                {
                    isProtected = true;
                }

                string? mime = (string?)rep.Attribute("mimeType") ?? (string?)adapt?.Attribute("mimeType") ?? "video/mp4";
                int? width = int.TryParse((string?)rep.Attribute("width"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) ? w : null;
                int? height = int.TryParse((string?)rep.Attribute("height"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ? h : null;
                long? bw = long.TryParse((string?)rep.Attribute("bandwidth"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long bVal) ? bVal : null;
                string? codecs = (string?)rep.Attribute("codecs") ?? (string?)adapt?.Attribute("codecs");

                var segmentUrls = new List<string>();
                string? repBase = rep.Elements().FirstOrDefault(e => e.Name.LocalName == "BaseURL")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(repBase) && Uri.TryCreate(effectiveBase, repBase, out var singleUri) &&
                    (singleUri.Scheme == Uri.UriSchemeHttp || singleUri.Scheme == Uri.UriSchemeHttps))
                {
                    segmentUrls.Add(singleUri.AbsoluteUri);
                }

                foreach (var segUrlEl in rep.Descendants().Where(e => e.Name.LocalName is "Initialization" or "SegmentURL"))
                {
                    string? media = (string?)segUrlEl.Attribute("sourceURL") ?? (string?)segUrlEl.Attribute("media");
                    if (!string.IsNullOrWhiteSpace(media) && Uri.TryCreate(effectiveBase, media, out var segUri) &&
                        (segUri.Scheme == Uri.UriSchemeHttp || segUri.Scheme == Uri.UriSchemeHttps))
                    {
                        segmentUrls.Add(segUri.AbsoluteUri);
                    }
                }

                string ext = mime.Contains("webm", StringComparison.OrdinalIgnoreCase) ? "webm" : "mp4";
                string quality = height is > 0 ? $"{height}p" : bw is > 0 ? $"{bw.Value / 1000} kbps" : "DASH Stream";
                string targetUrl = segmentUrls.FirstOrDefault() ?? baseUri.AbsoluteUri;
                variants.Add(new DiscoveredMediaVariant(
                    targetUrl,
                    $"DASH (.{ext})",
                    quality,
                    width,
                    height,
                    bw,
                    codecs,
                    isProtected,
                    segmentUrls));
            }
        }
        catch
        {
            // Malformed XML yields empty variants safely.
        }
        variants.Sort((a, b) => (b.Height ?? 0).CompareTo(a.Height ?? 0));
        return (isProtected, variants);
    }
}

public static class SiteGrabber
{
    private static readonly Regex AttrRegex = new(
        """(?:href|src|data-src|poster)\s*=\s*["']([^"'<>#\s]+)["']""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<IReadOnlyList<GrabbedSiteItem>> InspectUrlAsync(
        HttpClient client,
        string pageUrl,
        IReadOnlyList<CategoryRule> categories,
        CancellationToken token = default)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(baseUri.UserInfo))
            throw new ArgumentException("Enter a valid HTTP or HTTPS page URL without credentials.");

        using var request = new HttpRequestMessage(HttpMethod.Get, baseUri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        string mediaType = response.Content.Headers.ContentType?.MediaType ?? "";

        var results = new List<GrabbedSiteItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (HlsAndDashParser.IsHlsPlaylistText(body) || baseUri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            await AppendHlsVariantsAsync(client, baseUri, body, results, seen, token).ConfigureAwait(false);
            return results;
        }

        if (mediaType.Contains("dash+xml", StringComparison.OrdinalIgnoreCase) || baseUri.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
        {
            var (isProtected, variants) = HlsAndDashParser.ParseDashMpd(body, baseUri);
            foreach (var v in variants)
            {
                string name = SuggestMediaFileName(baseUri, v.QualityLabel, v.ContainerFormat.Contains("webm") ? ".webm" : ".mp4");
                results.Add(new GrabbedSiteItem(v.Url, name, "Video", v.ContainerFormat, $"{v.QualityLabel} · {v.ContainerFormat}", isProtected || v.IsProtected, v.SegmentUrls));
            }
            return results;
        }

        foreach (Match match in AttrRegex.Matches(body))
        {
            string raw = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!Uri.TryCreate(baseUri, raw, out var resolved) ||
                (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(resolved.UserInfo))
                continue;

            string abs = resolved.AbsoluteUri;
            if (!seen.Add(abs)) continue;

            if (resolved.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string hlsText = await client.GetStringAsync(resolved, token).ConfigureAwait(false);
                    await AppendHlsVariantsAsync(client, resolved, hlsText, results, seen, token).ConfigureAwait(false);
                    continue;
                }
                catch { /* Fall through to direct entry if manifest cannot be fetched */ }
            }
            else if (resolved.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string mpdText = await client.GetStringAsync(resolved, token).ConfigureAwait(false);
                    var (prot, variants) = HlsAndDashParser.ParseDashMpd(mpdText, resolved);
                    foreach (var v in variants)
                    {
                        string vName = SuggestMediaFileName(resolved, v.QualityLabel, v.ContainerFormat.Contains("webm") ? ".webm" : ".mp4");
                        results.Add(new GrabbedSiteItem(v.Url, vName, "Video", v.ContainerFormat, $"{v.QualityLabel} · {v.ContainerFormat}", prot || v.IsProtected, v.SegmentUrls));
                    }
                    continue;
                }
                catch { }
            }

            string fileName = SafeSuggestedName(resolved);
            string category = DownloadManager.ClassifyFileCategory(fileName, categories);
            string ext = Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant();
            string kind = string.IsNullOrEmpty(ext) ? "Web Resource" : $"{ext} File";
            results.Add(new GrabbedSiteItem(abs, fileName, category, kind, $"{category} · {kind}"));
        }

        return results;
    }

    private static async Task AppendHlsVariantsAsync(
        HttpClient client,
        Uri manifestUri,
        string manifestText,
        List<GrabbedSiteItem> results,
        HashSet<string> seen,
        CancellationToken token)
    {
        var (isMaster, isProtected, variants, directSegments) = HlsAndDashParser.ParseHls(manifestText, manifestUri);
        if (isMaster && variants.Count > 0)
        {
            foreach (var v in variants)
            {
                IReadOnlyList<string> segs = v.SegmentUrls;
                bool variantProtected = isProtected || v.IsProtected;
                try
                {
                    string childText = await client.GetStringAsync(v.Url, token).ConfigureAwait(false);
                    var parsedChild = HlsAndDashParser.ParseHls(childText, new Uri(v.Url));
                    segs = parsedChild.DirectSegments;
                    variantProtected |= parsedChild.IsProtected;
                }
                catch { }

                string name = SuggestMediaFileName(manifestUri, v.QualityLabel, ".ts");
                results.Add(new GrabbedSiteItem(v.Url, name, "Video", "HLS Stream", $"{v.QualityLabel} · HLS ({segs.Count} segments)", variantProtected, segs));
            }
        }
        else
        {
            string name = SuggestMediaFileName(manifestUri, "Stream", ".ts");
            results.Add(new GrabbedSiteItem(manifestUri.AbsoluteUri, name, "Video", "HLS Stream", $"HLS Stream ({directSegments.Count} segments)", isProtected, directSegments));
        }
    }

    internal static string SuggestMediaFileName(Uri uri, string quality, string defaultExt)
    {
        string rawStem = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.AbsolutePath.Split('/').LastOrDefault() ?? "video"));
        if (string.IsNullOrWhiteSpace(rawStem) || rawStem.Equals("master", StringComparison.OrdinalIgnoreCase) || rawStem.Equals("index", StringComparison.OrdinalIgnoreCase) || rawStem.Equals("playlist", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            rawStem = parts.Length >= 2 ? parts[^2] : "media";
        }
        string cleanQuality = Regex.Replace(quality ?? "", @"[^a-zA-Z0-9_-]", "");
        string candidate = string.IsNullOrWhiteSpace(cleanQuality) ? $"{rawStem}{defaultExt}" : $"{rawStem}-{cleanQuality}{defaultExt}";
        return DownloadManager.SanitizeFileName(candidate);
    }

    internal static string SafeSuggestedName(Uri uri)
    {
        string raw = Uri.UnescapeDataString(uri.AbsolutePath.Split('/').LastOrDefault() ?? "");
        return DownloadManager.SanitizeFileName(raw);
    }
}
