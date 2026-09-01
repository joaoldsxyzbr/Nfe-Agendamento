using System.Diagnostics;

namespace NfeAgendamento.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    public static IReadOnlyList<string> MenuLabels { get; } =
        ["Abrir Central", "Abrir sistema", "Configurar certificado", "Verificar atualização", "Iniciar com o Windows", "Sair"];

    private readonly NotifyIcon _trayIcon;
    private readonly CentralForm _centralForm;
    private bool _allowClose;

    public TrayApplicationContext(CentralStateService centralState)
    {
        _centralForm = new CentralForm(centralState);
        _centralForm.FormClosing += CentralFormClosing;

        var menu = new ContextMenuStrip();
        var openCentral = new ToolStripMenuItem("Abrir Central");
        openCentral.Click += (_, _) => OpenCentral();
        var open = new ToolStripMenuItem("Abrir sistema");
        open.Click += (_, _) => OpenSystem();
        var configure = new ToolStripMenuItem("Configurar certificado");
        configure.Click += (_, _) => OpenSystem();
        var checkUpdates = new ToolStripMenuItem("Verificar atualização");
        checkUpdates.Click += async (_, _) => await CheckForUpdatesAsync();
        var startup = new ToolStripMenuItem("Iniciar com o Windows") { CheckOnClick = true, Checked = StartupManager.IsEnabled() };
        startup.CheckedChanged += (_, _) =>
        {
            try { StartupManager.SetEnabled(startup.Checked, lanMode: false); }
            catch { startup.Checked = !startup.Checked; }
        };
        var exit = new ToolStripMenuItem("Sair");
        exit.Click += (_, _) => ExitApplication();
        menu.Items.Add(openCentral);
        menu.Items.Add(open);
        menu.Items.Add(configure);
        menu.Items.Add(checkUpdates);
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _trayIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application,
            Text = "NFe Agendamento - Central",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => OpenCentral();

        _centralForm.Show();
        _centralForm.Activate();
    }

    protected override void ExitThreadCore()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _centralForm.Dispose();
        base.ExitThreadCore();
    }

    private void CentralFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
            return;

        e.Cancel = true;
        _centralForm.Hide();
    }

    private void OpenCentral()
    {
        if (!_centralForm.Visible)
            _centralForm.Show();

        if (_centralForm.WindowState == FormWindowState.Minimized)
            _centralForm.WindowState = FormWindowState.Normal;

        _centralForm.Activate();
    }

    private static void OpenSystem()
    {
        try
        {
            Process.Start(new ProcessStartInfo(LocalHost.ListenUrl) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _centralForm.Close();
        ExitThread();
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
