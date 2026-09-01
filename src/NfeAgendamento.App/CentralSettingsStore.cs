using System.Text.Json;

namespace NfeAgendamento.App;

public sealed record CentralSettings(bool Enabled);

public sealed class CentralSettingsStore
{
    private readonly string _path;

    public CentralSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.StateRoot, "central.json");
    }

    public CentralSettings Load()
    {
        if (!File.Exists(_path))
            return new CentralSettings(true);

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<CentralSettings>(json) ?? new CentralSettings(true);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new CentralSettings(true);
        }
    }

    public void Save(CentralSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings));
        File.Move(tempPath, _path, overwrite: true);
    }
}
