using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ApexHUD.Core.Updates;

public sealed class UpdateCheckResult
{
    public bool Success { get; init; }
    public bool UpdateAvailable { get; init; }
    public string? ErrorMessage { get; init; }
    public Version CurrentVersion { get; init; } = new(1, 0, 0);
    public Version? LatestVersion { get; init; }
    public string LatestTag { get; init; } = "";
    public string ReleaseUrl { get; init; } = UpdateService.ReleasesPageUrl;
    public string ReleaseNotes { get; init; } = "";
    public string? InstallerUrl { get; init; }
    public long InstallerSize { get; init; }
    public string? PortableUrl { get; init; }
    public long PortableSize { get; init; }
}

/// <summary>
/// Zero-dependency GitHub Releases auto-updater.
/// Checks astarkulov/z-overlay latest release, downloads the matching asset (Inno Setup installer
/// for installed copies or single-file ApexHUD.exe for portable copies) with progress & PE/size
/// verification, and applies it seamlessly with automatic relaunch.
/// </summary>
public static class UpdateService
{
    public const string RepoOwner = "astarkulov";
    public const string RepoName = "z-overlay";
    public const string LatestApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
    public const string ReleasesPageUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly HttpClient ApiHttp = CreateHttpClient(TimeSpan.FromSeconds(8));
    private static readonly HttpClient DownloadHttp = CreateHttpClient(TimeSpan.FromMinutes(3));

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Z-Overlay-ApexHUD/{CurrentVersionString}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            if (v == null) return new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentVersionString =>
        $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static bool IsInstalledBuild()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;
        string? dir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(dir)) return false;
        return File.Exists(Path.Combine(dir, "unins000.exe"));
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var current = CurrentVersion;

        try
        {
            using var resp = await ApiHttp.GetAsync(LatestApiUrl, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseReleaseApiJson(json, current);
            }

            // If GitHub API rate-limited (403/429), fall back to non-API redirect on github.com/releases/latest
            if (resp.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
            {
                var fallback = await CheckViaRedirectFallbackAsync(current, ct).ConfigureAwait(false);
                if (fallback != null) return fallback;
            }

            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = current,
                ErrorMessage = $"GitHub HTTP {(int)resp.StatusCode}"
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = current,
                ErrorMessage = "Таймаут подключения к GitHub"
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = current,
                ErrorMessage = ex.Message
            };
        }
    }

    private static UpdateCheckResult ParseReleaseApiJson(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = root.TryGetProperty("tag_name", out var tagEl) ? (tagEl.GetString() ?? "") : "";
        string htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? (urlEl.GetString() ?? ReleasesPageUrl) : ReleasesPageUrl;
        string body = root.TryGetProperty("body", out var bodyEl) ? (bodyEl.GetString() ?? "") : "";

        if (!TryParseTagVersion(tag, out var latest))
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = current,
                ErrorMessage = $"Не удалось распознать версию релиза ({tag})"
            };
        }

        string? installerUrl = null;
        long installerSize = 0;
        string? portableUrl = null;
        long portableSize = 0;

        if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsEl.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";
                string dlUrl = asset.TryGetProperty("browser_download_url", out var dEl) ? (dEl.GetString() ?? "") : "";
                long size = asset.TryGetProperty("size", out var sEl) && sEl.TryGetInt64(out long sz) ? sz : 0;

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dlUrl)) continue;

                if (name.StartsWith("Z-Overlay-Setup", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    installerUrl = dlUrl;
                    installerSize = size;
                }
                else if (string.Equals(name, "ApexHUD.exe", StringComparison.OrdinalIgnoreCase))
                {
                    portableUrl = dlUrl;
                    portableSize = size;
                }
            }
        }

        return new UpdateCheckResult
        {
            Success = true,
            UpdateAvailable = latest > current,
            CurrentVersion = current,
            LatestVersion = latest,
            LatestTag = string.IsNullOrWhiteSpace(tag) ? $"v{latest.Major}.{latest.Minor}.{latest.Build}" : tag,
            ReleaseUrl = htmlUrl,
            ReleaseNotes = body,
            InstallerUrl = installerUrl,
            InstallerSize = installerSize,
            PortableUrl = portableUrl,
            PortableSize = portableSize
        };
    }

    private static async Task<UpdateCheckResult?> CheckViaRedirectFallbackAsync(Version current, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ReleasesPageUrl);
            using var resp = await ApiHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            string? finalUri = resp.RequestMessage?.RequestUri?.AbsoluteUri;
            if (string.IsNullOrEmpty(finalUri)) return null;

            const string marker = "/releases/tag/";
            int idx = finalUri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            string tag = finalUri[(idx + marker.Length)..].Trim('/');
            if (!TryParseTagVersion(tag, out var latest)) return null;

            string baseDl = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tag}";
            return new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = latest > current,
                CurrentVersion = current,
                LatestVersion = latest,
                LatestTag = tag,
                ReleaseUrl = finalUri,
                InstallerUrl = $"{baseDl}/Z-Overlay-Setup-{tag}.exe",
                PortableUrl = $"{baseDl}/ApexHUD.exe"
            };
        }
        catch
        {
            return null;
        }
    }

    public static bool TryParseTagVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        string clean = tag.Trim();
        if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            clean = clean[1..];

        int dash = clean.IndexOf('-');
        if (dash >= 0) clean = clean[..dash];

        if (!Version.TryParse(clean, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
        return true;
    }

    /// <summary>
    /// Downloads the appropriate update binary (installer or portable exe), verifies its size and PE header,
    /// spawns a detached PowerShell updater script (works under Restricted ExecutionPolicy via -EncodedCommand),
    /// and returns true when ready for the caller to cleanly exit the running app.
    /// </summary>
    public static async Task<(bool ReadyToExit, string? ErrorMessage)> DownloadAndLaunchUpdateAsync(
        UpdateCheckResult info,
        IProgress<(double Progress, long DownloadedBytes, long TotalBytes)>? progress,
        CancellationToken ct = default)
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return (false, "Не удалось определить путь текущего ApexHUD.exe.");

        string? exeDir = Path.GetDirectoryName(exePath);
        bool isInstalled = IsInstalledBuild();
        bool dirWritable = !string.IsNullOrEmpty(exeDir) && CanWriteDirectory(exeDir);

        bool useInstaller;
        string? downloadUrl;
        long expectedSize;
        string stagingFileName;

        if (isInstalled && !string.IsNullOrEmpty(info.InstallerUrl))
        {
            useInstaller = true;
            downloadUrl = info.InstallerUrl;
            expectedSize = info.InstallerSize;
            stagingFileName = $"Z-Overlay-Setup-{info.LatestTag}.exe";
        }
        else if (!isInstalled && dirWritable && !string.IsNullOrEmpty(info.PortableUrl))
        {
            useInstaller = false;
            downloadUrl = info.PortableUrl;
            expectedSize = info.PortableSize;
            stagingFileName = $"ApexHUD-{info.LatestTag}.exe";
        }
        else if (!string.IsNullOrEmpty(info.InstallerUrl))
        {
            useInstaller = true;
            downloadUrl = info.InstallerUrl;
            expectedSize = info.InstallerSize;
            stagingFileName = $"Z-Overlay-Setup-{info.LatestTag}.exe";
        }
        else
        {
            return (false, "В релизе GitHub не найден файл обновления (.exe).");
        }

        string stagingDir = Path.Combine(Path.GetTempPath(), "ApexHUD-Update");
        Directory.CreateDirectory(stagingDir);
        string stagedPath = Path.Combine(stagingDir, SanitizeFileName(stagingFileName));
        string partPath = stagedPath + ".part";

        try
        {
            using var resp = await DownloadHttp.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (false, $"Ошибка скачивания (HTTP {(int)resp.StatusCode}).");

            long totalBytes = resp.Content.Headers.ContentLength ?? expectedSize;
            long downloaded = 0;

            await using (var srcStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dstStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 32768, useAsync: true))
            {
                byte[] buffer = new byte[32768];
                int read;
                while ((read = await srcStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await dstStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    downloaded += read;
                    double frac = totalBytes > 0 ? Math.Clamp((double)downloaded / totalBytes, 0.0, 1.0) : 0.0;
                    progress?.Report((frac, downloaded, totalBytes));
                }
                await dstStream.FlushAsync(ct).ConfigureAwait(false);
            }

            if (downloaded < 32_768)
                return (false, "Скачанный файл повреждён (слишком маленький размер).");

            if (expectedSize > 0 && downloaded != expectedSize)
                return (false, $"Размер файла не совпадает ({downloaded} / {expectedSize} байт).");

            if (!HasPeHeader(partPath))
                return (false, "Скачанный файл не является исполняемым файлом Windows.");

            if (File.Exists(stagedPath))
                File.Delete(stagedPath);
            File.Move(partPath, stagedPath);

            progress?.Report((1.0, downloaded, Math.Max(totalBytes, downloaded)));

            SpawnDetachedUpdater(useInstaller, stagedPath, exePath, Environment.ProcessId);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            TryDelete(partPath);
            return (false, "Обновление отменено.");
        }
        catch (Exception ex)
        {
            TryDelete(partPath);
            return (false, $"Ошибка обновления: {ex.Message}");
        }
    }

    private static void SpawnDetachedUpdater(bool useInstaller, string stagedPath, string currentExePath, int currentPid)
    {
        string escSrc = EscapePsSingleQuote(stagedPath);
        string escDst = EscapePsSingleQuote(currentExePath);

        string psScript;
        if (useInstaller)
        {
            psScript =
                $"$pidOld = {currentPid}\n" +
                $"$setup = '{escSrc}'\n" +
                $"$dst = '{escDst}'\n" +
                "for ($i = 0; $i -lt 60; $i++) { if (-not (Get-Process -Id $pidOld -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 200 }\n" +
                "Start-Sleep -Milliseconds 250\n" +
                "Start-Process -FilePath $setup -ArgumentList '/SILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS' -Wait\n" +
                "Remove-Item -LiteralPath $setup -Force -ErrorAction SilentlyContinue\n" +
                "if (Test-Path -LiteralPath $dst) { Start-Process -FilePath $dst }\n";
        }
        else
        {
            psScript =
                "$ErrorActionPreference = 'Stop'\n" +
                $"$pidOld = {currentPid}\n" +
                $"$src = '{escSrc}'\n" +
                $"$dst = '{escDst}'\n" +
                "$bak = $dst + '.bak'\n" +
                "for ($i = 0; $i -lt 60; $i++) { if (-not (Get-Process -Id $pidOld -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 200 }\n" +
                "Start-Sleep -Milliseconds 250\n" +
                "$ok = $false\n" +
                "for ($i = 0; $i -lt 25; $i++) {\n" +
                "  try {\n" +
                "    if (Test-Path -LiteralPath $dst) { Copy-Item -LiteralPath $dst -Destination $bak -Force }\n" +
                "    Copy-Item -LiteralPath $src -Destination $dst -Force\n" +
                "    $ok = $true; break\n" +
                "  } catch { Start-Sleep -Milliseconds 300 }\n" +
                "}\n" +
                "if ($ok) {\n" +
                "  Remove-Item -LiteralPath $src -Force -ErrorAction SilentlyContinue\n" +
                "  Remove-Item -LiteralPath $bak -Force -ErrorAction SilentlyContinue\n" +
                "} elseif (Test-Path -LiteralPath $bak) {\n" +
                "  Copy-Item -LiteralPath $bak -Destination $dst -Force -ErrorAction SilentlyContinue\n" +
                "  Remove-Item -LiteralPath $bak -Force -ErrorAction SilentlyContinue\n" +
                "}\n" +
                "if (Test-Path -LiteralPath $dst) { Start-Process -FilePath $dst }\n";
        }

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process.Start(psi);
    }

    public static void OpenReleasesPage(string? url = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(url) ? ReleasesPageUrl : url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static bool HasPeHeader(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return fs.ReadByte() == 0x4D && fs.ReadByte() == 0x5A; // "MZ"
        }
        catch
        {
            return false;
        }
    }

    private static bool CanWriteDirectory(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".apexhud_write_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string EscapePsSingleQuote(string s) => s.Replace("'", "''");

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
