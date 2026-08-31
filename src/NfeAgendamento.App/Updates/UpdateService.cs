using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace NfeAgendamento.App.Updates;

public sealed record UpdateCheckResult(Version CurrentVersion, Version? LatestVersion)
{
    public bool IsUpdateAvailable => LatestVersion is not null && LatestVersion > CurrentVersion;
}

public sealed class UpdateService : IDisposable
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/joaoldsxyzbr/Nfe-Agendamento/releases/latest";
    private readonly HttpClient _httpClient;
    private readonly Version _currentVersion;

    public UpdateService(HttpClient? httpClient = null, Version? currentVersion = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NfeAgendamento-Updater/1.0");
        _currentVersion = currentVersion ?? Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new UpdateCheckResult(_currentVersion, null);

        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken)
            ?? throw new InvalidDataException("Resposta de atualização vazia.");
        var tag = release.TagName?.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var latestVersion))
            throw new InvalidDataException("Versão publicada inválida.");

        return new UpdateCheckResult(_currentVersion, latestVersion);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName);
}
