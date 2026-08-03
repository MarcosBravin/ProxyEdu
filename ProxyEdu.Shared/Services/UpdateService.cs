using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProxyEdu.Shared.Services;

public sealed class UpdateService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<UpdateService> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private GithubRelease? _latestRelease;
    private GithubReleaseAsset? _selectedAsset;
    private string? _downloadedPackagePath;
    private UpdatePackageType _downloadedPackageType = UpdatePackageType.Unknown;
    private long _downloadedBytes;
    private long _totalBytes;

    public UpdateService(
        IConfiguration configuration,
        ILogger<UpdateService> logger,
        IHostApplicationLifetime applicationLifetime,
        HttpClient httpClient)
    {
        _configuration = configuration;
        _logger = logger;
        _applicationLifetime = applicationLifetime;
        _httpClient = httpClient;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ProxyEdu-Updater/1.0");
        }
    }

    public UpdateStatus Status { get; private set; } = new();

    public Task CheckForUpdatesAsync() => CheckForUpdatesAsync(CancellationToken.None);

    public Task DownloadUpdateAsync() => DownloadUpdateAsync(CancellationToken.None);

    public Task InstallUpdateAsync() => InstallUpdateAsync(CancellationToken.None);

    public Task RestartApplicationAsync() => RestartApplicationAsync(CancellationToken.None);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!GetSettings().CheckOnStartup)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await CheckForUpdatesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao verificar atualizacoes ao iniciar");
            Status = Status with
            {
                State = UpdateState.Failed,
                Message = "Falha ao verificar atualizacoes.",
                Error = ex.Message,
                CheckedAtUtc = DateTime.UtcNow
            };
        }
    }

    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await CheckForUpdatesCoreAsync(cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task DownloadUpdateAsync(CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (_latestRelease is null)
            {
                await CheckForUpdatesCoreAsync(cancellationToken);
            }

            if (_selectedAsset is null || string.IsNullOrWhiteSpace(_selectedAsset.BrowserDownloadUrl))
            {
                throw new InvalidOperationException("Nenhum instalador ProxyEdu foi encontrado na última release.");
            }

            var updateDirectory = GetUpdateWorkingDirectory();
            Directory.CreateDirectory(updateDirectory);

            var packagePath = Path.Combine(updateDirectory, _selectedAsset.Name);
            var packageType = ResolvePackageType(_selectedAsset.Name);
            Status = Status with
            {
                State = UpdateState.Downloading,
                Message = $"Baixando {_selectedAsset.Name}.",
                ProgressPercent = 0,
                DownloadedBytes = 0,
                TotalBytes = 0,
                Error = null
            };

            using var response = await _httpClient.GetAsync(
                _selectedAsset.BrowserDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            _totalBytes = response.Content.Headers.ContentLength ?? 0;
            await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var local = File.Create(packagePath);

            var buffer = new byte[81920];
            _downloadedBytes = 0;
            int read;
            while ((read = await remote.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await local.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                _downloadedBytes += read;
                Status = Status with
                {
                    DownloadedBytes = _downloadedBytes,
                    TotalBytes = _totalBytes,
                    ProgressPercent = CalculateProgress(_downloadedBytes, _totalBytes)
                };
            }

            var expectedSha256 = Status.Sha256;
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actualSha256 = await CalculateSha256Async(packagePath, cancellationToken);
                if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(packagePath);
                    throw new InvalidOperationException("SHA256 do pacote baixado não confere com o valor informado na release.");
                }
            }

            _downloadedPackagePath = packagePath;
            _downloadedPackageType = packageType;

            Status = Status with
            {
                State = UpdateState.Downloaded,
                Message = packageType == UpdatePackageType.Installer
                    ? "Instalador baixado e pronto para execucao."
                    : "Pacote de atualização baixado.",
                DownloadedPackagePath = _downloadedPackagePath,
                PackageType = packageType.ToString(),
                ProgressPercent = 100,
                Error = null
            };

            _logger.LogInformation("Atualização baixada em {PackagePath}", packagePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = Status with
            {
                State = UpdateState.Failed,
                Message = "Falha ao baixar atualização.",
                Error = ex.Message
            };
            _logger.LogError(ex, "Falha ao baixar atualização");
            throw;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task InstallUpdateAsync(CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(_downloadedPackagePath) || !File.Exists(_downloadedPackagePath))
            {
                throw new InvalidOperationException("Nenhuma atualização baixada está pronta para instalação.");
            }

            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            {
                throw new InvalidOperationException("Não foi possível identificar o executável atual.");
            }

            var appDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var backupDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ProxyEdu",
                "backups",
                $"{Path.GetFileNameWithoutExtension(processPath)}-{DateTime.UtcNow:yyyyMMddHHmmss}");

            var scriptPath = _downloadedPackageType == UpdatePackageType.Installer
                ? CreateInstallerRunnerScript(appDirectory, _downloadedPackagePath, backupDirectory, processPath, GetSettings().InstallerArguments)
                : CreateUnsupportedPackageScript(appDirectory, _downloadedPackagePath, backupDirectory, processPath);

            Status = Status with
            {
                State = UpdateState.Installing,
                Message = "Instalação preparada. A aplicação será encerrada para executar o instalador.",
                BackupDirectory = backupDirectory,
                Error = null
            };

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ProcessId {Environment.ProcessId}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? appDirectory
            };

            Process.Start(startInfo);
            _logger.LogWarning("Instalação de atualização iniciada. Backup: {BackupDirectory}", backupDirectory);

            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
                await RestartApplicationAsync(CancellationToken.None);
            }, CancellationToken.None);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public Task RestartApplicationAsync(CancellationToken cancellationToken)
    {
        Status = Status with
        {
            State = UpdateState.Restarting,
            Message = "Encerrando aplicação para concluir a atualização."
        };

        _applicationLifetime.StopApplication();
        return Task.CompletedTask;
    }

    private async Task CheckForUpdatesCoreAsync(CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        if (string.IsNullOrWhiteSpace(settings.Owner) || string.IsNullOrWhiteSpace(settings.Repository))
        {
            Status = Status with
            {
                State = UpdateState.Disabled,
                Message = "Atualizador sem repositorio configurado.",
                CheckedAtUtc = DateTime.UtcNow
            };
            _logger.LogInformation("Atualizador sem repositorio configurado.");
            return;
        }

        Status = Status with
        {
            State = UpdateState.Checking,
            Message = "Verificando atualizacoes.",
            Error = null,
            CheckedAtUtc = DateTime.UtcNow
        };

        var release = await GetLatestReleaseAsync(settings, cancellationToken);
        var current = ResolveCurrentVersion(settings);
        var latest = SemanticVersion.Parse(release.TagName);
        var updateAvailable = latest.CompareTo(current) > 0;
        var asset = SelectReleaseAsset(release);

        _latestRelease = release;
        _selectedAsset = asset;

        Status = Status with
        {
            State = updateAvailable ? UpdateState.Available : UpdateState.UpToDate,
            Message = updateAvailable
                ? $"Atualização disponível: {release.TagName}"
                : "Aplicação atualizada.",
            CurrentVersion = current.ToString(),
            LatestVersion = latest.ToString(),
            LatestTag = release.TagName,
            Changelog = release.Body ?? string.Empty,
            PublishedAtUtc = release.PublishedAt,
            AssetName = asset?.Name,
            AssetUrl = asset?.BrowserDownloadUrl,
            Sha256 = ResolveSha256(release, asset),
            PackageType = asset is null ? "" : ResolvePackageType(asset.Name).ToString(),
            ProgressPercent = 0,
            DownloadedBytes = 0,
            TotalBytes = 0,
            CheckedAtUtc = DateTime.UtcNow,
            Error = null
        };

        if (updateAvailable)
        {
            _logger.LogWarning(
                "Atualização disponível: {CurrentVersion} -> {LatestVersion}. Changelog: {Changelog}",
                current,
                latest,
                release.Body);
        }
        else
        {
            _logger.LogInformation("Nenhuma atualização disponível. Versão atual: {Version}", current);
        }
    }

    private async Task<GithubRelease> GetLatestReleaseAsync(UpdaterSettings settings, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{settings.Owner}/{settings.Repository}/releases/latest";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GithubRelease>(stream, cancellationToken: cancellationToken);
        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            throw new InvalidOperationException("Resposta invalida da API de releases do GitHub.");
        }

        return release;
    }

    private UpdaterSettings GetSettings()
    {
        var checkOnStartupText = _configuration["Updater:CheckOnStartup"];
        return new UpdaterSettings
        {
            Owner = _configuration["Updater:Owner"] ?? "",
            Repository = _configuration["Updater:Repository"] ?? "",
            CurrentVersion = _configuration["Updater:CurrentVersion"] ?? "AUTO",
            InstallerArguments = _configuration["Updater:InstallerArguments"] ?? "/S",
            CheckOnStartup = string.IsNullOrWhiteSpace(checkOnStartupText) ||
                bool.TryParse(checkOnStartupText, out var checkOnStartup) && checkOnStartup
        };
    }

    private static GithubReleaseAsset? SelectReleaseAsset(GithubRelease release)
    {
        return release.Assets
            .Where(a => !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl))
            .OrderByDescending(a => string.Equals(a.Name, "ProxyEduInstaller.exe", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static SemanticVersion ResolveCurrentVersion(UpdaterSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CurrentVersion) &&
            !string.Equals(settings.CurrentVersion, "AUTO", StringComparison.OrdinalIgnoreCase))
        {
            return SemanticVersion.Parse(settings.CurrentVersion);
        }

        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return SemanticVersion.Parse(informational ?? assembly.GetName().Version?.ToString() ?? "0.0.0");
    }

    private static string? ResolveSha256(GithubRelease release, GithubReleaseAsset? asset)
    {
        if (asset is not null && !string.IsNullOrWhiteSpace(asset.Digest))
        {
            var digest = asset.Digest.Trim();
            return digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..]
                : digest;
        }

        if (string.IsNullOrWhiteSpace(release.Body))
        {
            return null;
        }

        if (asset is not null)
        {
            var escapedName = Regex.Escape(asset.Name);
            var assetMatch = Regex.Match(
                release.Body,
                $"{escapedName}[^A-Fa-f0-9]{{1,80}}([A-Fa-f0-9]{{64}})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (assetMatch.Success)
            {
                return assetMatch.Groups[1].Value;
            }
        }

        var genericMatch = Regex.Match(
            release.Body,
            @"(?:sha256|sha-256)[^A-Fa-f0-9]{1,20}([A-Fa-f0-9]{64})",
            RegexOptions.IgnoreCase);
        return genericMatch.Success ? genericMatch.Groups[1].Value : null;
    }

    private static async Task<string> CalculateSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static int CalculateProgress(long downloaded, long total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return (int)Math.Clamp(downloaded * 100 / total, 0, 100);
    }

    private static string GetUpdateWorkingDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ProxyEdu",
            "updates");
    }

    private static UpdatePackageType ResolvePackageType(string assetName)
    {
        if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return UpdatePackageType.Installer;
        }

        if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return UpdatePackageType.Zip;
        }

        return UpdatePackageType.Unknown;
    }

    private static string CreateInstallerRunnerScript(
        string appDirectory,
        string installerPath,
        string backupDirectory,
        string executablePath,
        string installerArguments)
    {
        var scriptDirectory = GetUpdateWorkingDirectory();
        Directory.CreateDirectory(scriptDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(backupDirectory)!);
        var scriptPath = Path.Combine(scriptDirectory, "install-update.ps1");
        var logPath = Path.Combine(scriptDirectory, "install-update.log");

        var script = $$"""
param([int]$ProcessId)
$ErrorActionPreference = 'Stop'
$appDir = {{ToPowerShellString(appDirectory)}}
$installerPath = {{ToPowerShellString(installerPath)}}
$installerArguments = {{ToPowerShellString(installerArguments)}}
$backupDir = {{ToPowerShellString(backupDirectory)}}
$exePath = {{ToPowerShellString(executablePath)}}
$logPath = {{ToPowerShellString(logPath)}}

function Write-InstallLog([string]$message) {
    $line = "$(Get-Date -Format o) $message"
    Add-Content -LiteralPath $logPath -Value $line
}

try {
    Write-InstallLog "Aguardando processo $ProcessId encerrar."
    Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    Write-InstallLog "Criando backup em $backupDir."
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    Copy-Item -Path (Join-Path $appDir '*') -Destination $backupDir -Recurse -Force

    Write-InstallLog "Executando instalador $installerPath $installerArguments."
    $installer = Start-Process -FilePath $installerPath -ArgumentList $installerArguments -Wait -PassThru
    if ($installer.ExitCode -ne 0) {
        throw "Instalador retornou codigo $($installer.ExitCode)."
    }

    Write-InstallLog "Instalador finalizado com sucesso."
    Write-InstallLog "Atualização concluída."
}
catch {
    Write-InstallLog "Falha na atualização: $($_.Exception.Message)"
    try {
        if (Test-Path -LiteralPath $backupDir) {
            Write-InstallLog "Restaurando backup."
            Copy-Item -Path (Join-Path $backupDir '*') -Destination $appDir -Recurse -Force
            Start-Process -FilePath $exePath -WorkingDirectory $appDir
        }
    }
    catch {
        Write-InstallLog "Falha ao restaurar backup: $($_.Exception.Message)"
    }
}
""";

        File.WriteAllText(scriptPath, script, Encoding.UTF8);
        return scriptPath;
    }

    private static string CreateUnsupportedPackageScript(
        string appDirectory,
        string packagePath,
        string backupDirectory,
        string executablePath)
    {
        throw new InvalidOperationException(
            $"O pacote '{Path.GetFileName(packagePath)}' não é um instalador executável. Publique ProxyEduInstaller.exe nos assets da release.");
    }

    private static string ToPowerShellString(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}

public sealed record UpdateStatus
{
    public UpdateState State { get; init; } = UpdateState.Idle;
    public string Message { get; init; } = "Atualizador aguardando.";
    public string CurrentVersion { get; init; } = "";
    public string LatestVersion { get; init; } = "";
    public string LatestTag { get; init; } = "";
    public string Changelog { get; init; } = "";
    public DateTime? PublishedAtUtc { get; init; }
    public string? AssetName { get; init; }
    public string? AssetUrl { get; init; }
    public string? Sha256 { get; init; }
    public string PackageType { get; init; } = "";
    public int ProgressPercent { get; init; }
    public long DownloadedBytes { get; init; }
    public long TotalBytes { get; init; }
    public string? DownloadedPackagePath { get; init; }
    public string? BackupDirectory { get; init; }
    public DateTime? CheckedAtUtc { get; init; }
    public string? Error { get; init; }
}

public enum UpdateState
{
    Idle,
    Disabled,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Downloaded,
    Installing,
    Restarting,
    Failed
}

public sealed class UpdaterSettings
{
    public string Owner { get; set; } = "";
    public string Repository { get; set; } = "";
    public string CurrentVersion { get; set; } = "AUTO";
    public bool CheckOnStartup { get; set; } = true;
    public string InstallerArguments { get; set; } = "/S";
}

internal enum UpdatePackageType
{
    Unknown,
    Installer,
    Zip
}

internal sealed class GithubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("assets")]
    public List<GithubReleaseAsset> Assets { get; set; } = new();

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }
}

internal sealed class GithubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}

internal sealed record SemanticVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<SemanticVersion>
{
    public static SemanticVersion Parse(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var buildIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        if (buildIndex >= 0)
        {
            normalized = normalized[..buildIndex];
        }

        string? preRelease = null;
        var preReleaseIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        if (preReleaseIndex >= 0)
        {
            preRelease = normalized[(preReleaseIndex + 1)..];
            normalized = normalized[..preReleaseIndex];
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return new SemanticVersion(
            ParsePart(parts, 0),
            ParsePart(parts, 1),
            ParsePart(parts, 2),
            preRelease);
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0) return minor;
        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0) return patch;

        if (string.IsNullOrWhiteSpace(PreRelease) && string.IsNullOrWhiteSpace(other.PreRelease)) return 0;
        if (string.IsNullOrWhiteSpace(PreRelease)) return 1;
        if (string.IsNullOrWhiteSpace(other.PreRelease)) return -1;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public override string ToString()
    {
        var version = $"{Major}.{Minor}.{Patch}";
        return string.IsNullOrWhiteSpace(PreRelease) ? version : $"{version}-{PreRelease}";
    }

    private static int ParsePart(string[] parts, int index)
    {
        if (parts.Length <= index)
        {
            return 0;
        }

        var match = Regex.Match(parts[index], @"^\d+");
        return match.Success && int.TryParse(match.Value, out var value) ? value : 0;
    }

    private static int ComparePreRelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var length = Math.Max(leftParts.Length, rightParts.Length);

        for (var i = 0; i < length; i++)
        {
            if (i >= leftParts.Length) return -1;
            if (i >= rightParts.Length) return 1;

            var leftNumeric = int.TryParse(leftParts[i], out var leftNumber);
            var rightNumeric = int.TryParse(rightParts[i], out var rightNumber);

            if (leftNumeric && rightNumeric)
            {
                var numberCompare = leftNumber.CompareTo(rightNumber);
                if (numberCompare != 0) return numberCompare;
                continue;
            }

            if (leftNumeric) return -1;
            if (rightNumeric) return 1;

            var textCompare = string.CompareOrdinal(leftParts[i], rightParts[i]);
            if (textCompare != 0) return textCompare;
        }

        return 0;
    }
}
