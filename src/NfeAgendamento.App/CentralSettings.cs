using System.Text.Json;

namespace NfeAgendamento.App;

public sealed record CentralSettings(bool Enabled);

public sealed class CentralSettingsStore
{
    private readonly string _path;

    public CentralSettingsStore()
        : this(Path.Combine(AppPaths.StateRoot, "central.json"))
    {
    }

    public CentralSettingsStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Caminho de configuração inválido.", nameof(path));

        _path = path;
    }

    public CentralSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new CentralSettings(true);

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<CentralSettings>(json) ?? new CentralSettings(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
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

public sealed class CentralStateService
{
    private readonly object _sync = new();
    private readonly CentralSettingsStore _store;
    private bool _enabled;

    public CentralStateService(CentralSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _enabled = _store.Load().Enabled;
    }

    public bool IsEnabled
    {
        get
        {
            lock (_sync) return _enabled;
        }
    }

    public event EventHandler? Changed;

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_enabled == enabled)
                return;

            _store.Save(new CentralSettings(enabled));
            _enabled = enabled;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
