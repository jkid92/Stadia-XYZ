using System.Net.Http.Headers;
using System.Text.Json;

namespace StadiaX.ControlCenter;

internal sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);
internal sealed record ReleaseInfo(string Tag, string Url, DateTimeOffset? PublishedAt, IReadOnlyList<ReleaseAsset> Assets);

internal sealed class ReleaseChecker
{
    private static readonly Uri ReleasesUri = new("https://api.github.com/repos/jkid92/Stadia-XYZ/releases?per_page=30");

    public async Task<ReleaseInfo> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(ReleasesUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var release = json.RootElement.EnumerateArray().FirstOrDefault(candidate =>
            candidate.TryGetProperty("tag_name", out var tag) &&
            (tag.GetString() ?? "").StartsWith("windows-native-v", StringComparison.OrdinalIgnoreCase));
        if (release.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("No Windows Native release was found.");
        }

        return ParseRelease(release);
    }

    internal static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StadiaX-WindowsNative", "1.0"));
        return client;
    }

    internal static ReleaseInfo ParseRelease(JsonElement release)
    {
        var tag = release.TryGetProperty("tag_name", out var tagNode) ? tagNode.GetString() ?? "unknown" : "unknown";
        var url = release.TryGetProperty("html_url", out var urlNode) ? urlNode.GetString() ?? "" : "";
        DateTimeOffset? publishedAt = null;
        if (release.TryGetProperty("published_at", out var publishedNode) && DateTimeOffset.TryParse(publishedNode.GetString(), out var parsedPublishedAt)) publishedAt = parsedPublishedAt;
        var assets = new List<ReleaseAsset>();
        if (release.TryGetProperty("assets", out var assetsNode) && assetsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsNode.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "" : "";
                var downloadUrl = asset.TryGetProperty("browser_download_url", out var downloadNode) ? downloadNode.GetString() ?? "" : "";
                var size = asset.TryGetProperty("size", out var sizeNode) && sizeNode.TryGetInt64(out var parsedSize) ? parsedSize : 0;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(downloadUrl)) assets.Add(new ReleaseAsset(name, downloadUrl, size));
            }
        }
        return new ReleaseInfo(tag, url, publishedAt, assets);
    }
}
