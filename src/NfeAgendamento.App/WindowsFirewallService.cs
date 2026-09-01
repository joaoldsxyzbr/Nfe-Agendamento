using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace NfeAgendamento.App;

public sealed class WindowsFirewallService
{
    public const string RuleName = "NFe Agendamento Central";

    public async Task<FirewallRuleStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return FirewallRuleStatus.Unavailable;

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return FirewallRuleStatus.Unavailable;

        try
        {
            var exitCode = await RunPowerShellAsync(
                BuildCheckRuleScript(executable),
                elevated: false,
                cancellationToken);

            return exitCode switch
            {
                0 => FirewallRuleStatus.Configured,
                1 => FirewallRuleStatus.Missing,
                _ => FirewallRuleStatus.Unavailable
            };
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return FirewallRuleStatus.Unavailable;
        }
    }

    public async Task<bool> EnsureRuleAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return false;

        try
        {
            return await RunPowerShellAsync(
                BuildEnsureRuleScript(executable),
                elevated: true,
                cancellationToken) == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // Usuário cancelou a solicitação do UAC.
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return false;
        }
    }

    public static string BuildEnsureRuleScript(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
            throw new ArgumentException("Executável inválido.", nameof(executable));

        var escapedProgram = EscapePowerShellLiteral(executable);
        var escapedRuleName = EscapePowerShellLiteral(RuleName);

        return "$ErrorActionPreference='Stop'; "
            + $"$name='{escapedRuleName}'; $program='{escapedProgram}'; "
            + "$existing=Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue; "
            + "if($existing){$existing | Remove-NetFirewallRule -ErrorAction Stop}; "
            + $"New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol TCP -LocalPort {LocalHost.Port} -Profile Private -Program $program -Enabled True -ErrorAction Stop | Out-Null; "
            + "exit 0";
    }

    internal static string BuildCheckRuleScript(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
            throw new ArgumentException("Executável inválido.", nameof(executable));

        var escapedProgram = EscapePowerShellLiteral(executable);
        var escapedRuleName = EscapePowerShellLiteral(RuleName);

        return "$ErrorActionPreference='SilentlyContinue'; "
            + $"$name='{escapedRuleName}'; $program='{escapedProgram}'; "
            + "$rules=Get-NetFirewallRule -DisplayName $name | Where-Object { $_.Enabled -eq 'True' -and $_.Direction -eq 'Inbound' -and $_.Action -eq 'Allow' -and (([int]$_.Profile -band 2) -ne 0) }; "
            + "foreach($rule in $rules){ "
            + "$port=$rule | Get-NetFirewallPortFilter; $app=$rule | Get-NetFirewallApplicationFilter; "
            + $"if($port.Protocol -eq 'TCP' -and [string]$port.LocalPort -eq '{LocalHost.Port}' -and $app.Program -ieq $program){{exit 0}} "
            + "}; exit 1";
    }

    private static async Task<int> RunPowerShellAsync(
        string script,
        bool elevated,
        CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = elevated,
            CreateNoWindow = !elevated,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (!elevated)
        {
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
        }
        else
        {
            startInfo.Verb = "runas";
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o PowerShell.");

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
