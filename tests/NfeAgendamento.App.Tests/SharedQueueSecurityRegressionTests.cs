using System.Diagnostics;
using NfeAgendamento.App;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueSecurityRegressionTests
{
    [Fact]
    public void Production_interface_exposes_no_pairing_or_central_flow()
    {
        var program = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Program.cs"));
        var index = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "index.html"));

        Assert.DoesNotContain("/api/pairing/", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedQueueCentralService", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedQueueClient", program, StringComparison.Ordinal);
        Assert.DoesNotContain("/pairing.js", index, StringComparison.Ordinal);
        Assert.DoesNotContain("pairingCode", index, StringComparison.Ordinal);
        Assert.Contains("sharedFolderStatus", index, StringComparison.Ordinal);
    }

    [Fact]
    public void Fiscal_lock_path_is_confined_to_shared_status_directory()
    {
        var root = NewRoot();
        try
        {
            var paths = new SharedQueuePaths(root);
            Assert.Equal(Path.Combine(root, "status", "fiscal.lock"), paths.FiscalLockPath);
            Assert.Throws<ArgumentException>(() => paths.StatusPath("..\\fora.lock"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reparse_point_inside_operational_tree_is_rejected()
    {
        var root = NewRoot();
        var outside = NewRoot();
        try
        {
            var paths = new SharedQueuePaths(root);
            paths.InitializeForSharedUse();

            Directory.Delete(paths.StatusDirectory);
            CreateJunction(paths.StatusDirectory, outside);

            Assert.False(paths.ValidateForClient());
        }
        finally
        {
            TryDeleteLink(Path.Combine(root, "status"));
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Startup_menu_does_not_use_recursive_checked_changed_rollback()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TrayApplicationContext.cs"));

        Assert.DoesNotContain("startup.CheckedChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("CheckOnClick = false", source, StringComparison.Ordinal);
    }

    private static void CreateJunction(string junction, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(junction);
        start.ArgumentList.Add(target);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível criar junction de teste.");
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static void TryDeleteLink(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path);
        }
        catch
        {
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-agendamento-security-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
