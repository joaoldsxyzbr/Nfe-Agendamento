using System.Diagnostics;

namespace NfeAgendamento.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    public static IReadOnlyList<string> MenuLabels { get; } =
        ["Abrir sistema", "Configurar certificado", "Verificar atualização", "Sair"];

    private readonly NotifyIcon _trayIcon;
    private readonly string _listenUrl;

    public TrayApplicationContext(string listenUrl)
    {
        _listenUrl = listenUrl;

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Abrir sistema");
        open.Click += (_, _) => OpenSystem();
        var configure = new ToolStripMenuItem("Configurar certificado");
        configure.Click += (_, _) => OpenSystem();
        var checkUpdates = new ToolStripMenuItem("Verificar atualização");
        checkUpdates.Click += async (_, _) => await CheckForUpdatesAsync();
        var exit = new ToolStripMenuItem("Sair");
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(open);
        menu.Items.Add(configure);
        menu.Items.Add(checkUpdates);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "NFe Agendamento",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => OpenSystem();
    }

    protected override void ExitThreadCore()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.ExitThreadCore();
    }

    private void OpenSystem()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_listenUrl) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            using var service = new Updates.UpdateService();
            var result = await service.CheckAsync();
            var message = result.IsUpdateAvailable
                ? $"Há uma versão nova disponível: {result.LatestVersion}. Abra o repositório para baixar."
                : "Você já está usando a versão mais recente disponível.";
            MessageBox.Show(message, "NFe Agendamento", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
        {
            MessageBox.Show(
                "Não foi possível verificar atualizações agora. Tente novamente mais tarde.",
                "NFe Agendamento",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
