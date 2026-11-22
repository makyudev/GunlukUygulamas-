using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace GunlukUygulamasi.Services;

public sealed class UpdateService
{
    private const string ReleasesEndpoint =
        "https://api.github.com/repos/makyudev/GunlukUygulamas-/releases/latest";
    private static readonly Uri ReleaseAssetsBaseUri =
        new("https://github.com/makyudev/GunlukUygulamas-/releases/download/");
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    public async Task<UpdateRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        using var response = await HttpClient.GetAsync(ReleasesEndpoint, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: timeout.Token);
        var root = document.RootElement;

        if (!root.TryGetProperty("tag_name", out var tagElement) ||
            tagElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("GitHub Release sürüm etiketi içermiyor.");
        }

        var tagName = tagElement.GetString()!;
        var versionText = tagName.StartsWith('v') ? tagName[1..] : tagName;
        if (!Version.TryParse(versionText, out var parsedVersion))
        {
            throw new InvalidDataException($"GitHub Release sürüm etiketi geçersiz: {tagName}");
        }

        var version = new Version(parsedVersion.Major, parsedVersion.Minor, Math.Max(parsedVersion.Build, 0));
        var releaseNotes = root.TryGetProperty("body", out var bodyElement) &&
                           bodyElement.ValueKind == JsonValueKind.String
            ? bodyElement.GetString() ?? string.Empty
            : string.Empty;

        return new UpdateRelease(version, tagName, releaseNotes, GetInstallerDownloadUrl(root));
    }

    public async Task<UpdateRelease?> GetAvailableUpdateAsync(CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        return release is not null && release.Version > CurrentVersion ? release : null;
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateRelease release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (release.InstallerDownloadUrl is null)
        {
            throw new InvalidOperationException("Bu sürüm için kurulum dosyası bulunamadı.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));

        var destinationPath = Path.Combine(
            Path.GetTempPath(), $"GunlukUygulamasi-Update-{Guid.NewGuid():N}.exe");
        var completed = false;
        try
        {
            using var response = await HttpClient.GetAsync(
                release.InstallerDownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var destination = new FileStream(
                destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long bytesReceived = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), timeout.Token);
                bytesReceived += bytesRead;
                progress?.Report(new DownloadProgress(bytesReceived, totalBytes));
            }

            completed = true;
            return destinationPath;
        }
        finally
        {
            if (!completed && File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GunlukUygulamasi/1.0");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static Version ReadCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version
            ?? throw new InvalidOperationException("Uygulama sürümü assembly bilgisinde tanımlı değil.");
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    private static Uri? GetInstallerDownloadUrl(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assetsElement) ||
            assetsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var executableAssets = assetsElement.EnumerateArray()
            .Where(asset =>
                asset.ValueKind == JsonValueKind.Object &&
                asset.TryGetProperty("name", out var nameElement) &&
                nameElement.ValueKind == JsonValueKind.String &&
                nameElement.GetString()!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                asset.TryGetProperty("browser_download_url", out var urlElement) &&
                urlElement.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                ReleaseAssetsBaseUri.IsBaseOf(uri))
            .ToArray();

        var setupAssets = executableAssets
            .Where(asset =>
            {
                var name = asset.GetProperty("name").GetString()!;
                return name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("kurulum", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        var preferredAsset = setupAssets.FirstOrDefault(asset =>
            string.Equals(asset.GetProperty("name").GetString(), "GunlukUygulamasi-Setup.exe",
                StringComparison.OrdinalIgnoreCase));
        var selectedAsset = preferredAsset.ValueKind == JsonValueKind.Object
            ? preferredAsset
            : setupAssets.Length == 1 ? setupAssets[0] : default;

        if (selectedAsset.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new Uri(selectedAsset.GetProperty("browser_download_url").GetString()!);
    }
}

public sealed record UpdateRelease(Version Version, string TagName, string ReleaseNotes, Uri? InstallerDownloadUrl);

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes);
