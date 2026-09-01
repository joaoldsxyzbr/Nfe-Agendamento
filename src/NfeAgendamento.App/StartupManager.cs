using Microsoft.Win32;

namespace NfeAgendamento.App;

public static class StartupManager
{
    private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "NfeAgendamento";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static string BuildStartupCommand(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
            throw new ArgumentException("Executável inválido.", nameof(executable));

        return $"\"{executable}\"";
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (key is null)
            throw new InvalidOperationException("Não foi possível configurar a inicialização do Windows.");

        if (enabled)
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Não foi possível localizar o executável do aplicativo.");
            key.SetValue(ValueName, BuildStartupCommand(executable));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
