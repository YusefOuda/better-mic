using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace BetterMic;

// Checks GitHub Releases for a newer version. Release builds get their version from the git tag
// (-p:Version in release.yml); local builds are 0.0.0 and never check.
static class UpdateChecker
{
    const string LatestReleaseApi = "https://api.github.com/repos/YusefOuda/better-mic/releases/latest";
    public const string ReleasesPage = "https://github.com/YusefOuda/better-mic/releases/latest";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub's API rejects requests without a User-Agent
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("better-mic", CurrentVersion?.ToString() ?? "dev"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static Version? CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version;

    static bool IsDevBuild(Version v) => Normalize(v) == new Version(0, 0, 0, 0);

    // Returns the tag of a newer release, or null if up to date, a dev build, or the check failed
    public static async Task<string?> CheckAsync()
    {
        var current = CurrentVersion;
        if (current == null || IsDevBuild(current)) return null;
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestReleaseApi));
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            return tag != null && IsNewer(tag, current) ? tag : null;
        }
        catch
        {
            return null;
        }
    }

    // Tags look like "v1.2.3". Tags that aren't plain versions (e.g. "v1.2.0-beta") are ignored.
    public static bool IsNewer(string tag, Version current) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var latest) && Normalize(latest) > Normalize(current);

    // Version("1.2.3") has Revision -1, which would compare lower than the assembly's 1.2.3.0
    static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
