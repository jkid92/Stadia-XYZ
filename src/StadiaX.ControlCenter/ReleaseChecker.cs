using System.Net.Http.Headers;
using System.Text.Json;

namespace StadiaX.ControlCenter;

internal sealed record ReleaseInfo(string Tag, string Url);

internal sealed class ReleaseChecker
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/jkid92/Stadia-XYZ/releases/latest");

    public async Task<ReleaseInfo> GetLatestAsync()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StadiaX-ControlCenter", "1.0"));
        using var response = await client.GetAsync(LatestReleaseUri).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var tag = json.RootElement.TryGetProperty("tag_name", out var tagNode) ? tagNode.GetString() ?? "unknown" : "unknown";
        var url = json.RootElement.TryGetProperty("html_url", out var urlNode) ? urlNode.GetString() ?? "" : "";
        return new ReleaseInfo(tag, url);
    }
}
