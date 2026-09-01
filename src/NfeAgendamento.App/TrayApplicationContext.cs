using System.Diagnostics;

namespace NfeAgendamento.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    public static IReadOnlyList<string> MenuLabels { get; } =
        ["Abrir Central", "Abrir sistema", "Copiar endereço da Central", "Configurar certificado", "Verificar atualização", "Iniciar com o Windows", "Sair"];

    private readonly NotifyIcon _trayIcon;
    private readonly CentralForm _centralForm;
    private readonly CentralStateService _centralState;
    private readonly ToolStripMenuItem _accessAddress;
    private readonly ToolStripMenuItem _copyAccessAddress;
    private bool _allowClose;

    public TrayApplicationContext(CentralStateService centralState)
    {
        _centralState = centralState ?? throw new ArgumentNullException(nameof(centralState));
        _centralForm = new CentralForm(_centralState);
        _centralForm.FormClosing += CentralFormClosing;

        var menu = new ContextMenuStrip();
        var openCentral = new ToolStripMenuItem("Abrir Central");
        openCentral.Click += (_, _) => OpenCentral();
        var open = new ToolStripMenuItem("Abrir sistema");
        open.Click += (_, _) => OpenSystem();
        _accessAddress = new ToolStripMenuItem { Enabled = false };
        _copyAccessAddress = new ToolStripMenuItem("Copiar endereço da Central");
        _copyAccessAddress.Click += (_, _) => CopyAccessAddress();
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
        menu.Items.Add(_accessAddress);
        menu.Items.Add(_copyAccessAddress);
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

        _centralState.Changed += CentralStateChanged;
        RefreshAccessMenu();

        _centralForm.Show();
        _centralForm.Activate();
    }

    public static string BuildAccessMenuText(bool enabled, string accessUrl)
    {
        if (!enabled)
            return "Acesso pela rede: desativado";

        if (string.IsNullOrWhiteSpace(accessUrl) || string.Equals(accessUrl, LocalHost.ListenUrl, StringComparison.OrdinalIgnoreCase))
            return "Acesso pela rede: IP não identificado";

        return $"Acesso: {accessUrl}";
    }

    protected override void ExitThreadCore()
    {
        _centralState.Changed -= CentralStateChanged;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _centralForm.Dispose();
        base.ExitThreadCore();
    }

    private void CentralStateChanged(object? sender, EventArgs e)
    {
        if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _trayIcon.ContextMenuStrip.BeginInvoke(new Action(RefreshAccessMenu));
            return;
        }

        RefreshAccessMenu();
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

    private void RefreshAccessMenu()
    {
        var enabled = _centralState.IsEnabled;
        var accessUrl = CentralNetworkInfo.GetAccessUrl(enabled);
        var shareable = enabled
            && !string.IsNullOrWhiteSpace(accessUrl)
            && !string.Equals(accessUrl, LocalHost.ListenUrl, StringComparison.OrdinalIgnoreCase);

        _accessAddress.Text = BuildAccessMenuText(enabled, accessUrl);
        _copyAccessAddress.Enabled = shareable;
    }

    private void CopyAccessAddress()
    {
        var accessUrl = CentralNetworkInfo.GetAccessUrl(_centralState.IsEnabled);
        if (!_centralState.IsEnabled || string.Equals(accessUrl, LocalHost.ListenUrl, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Inicie a Central e confirme que o IP da rede foi identificado antes de copiar o endereço.",
                "NFe Agendamento",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Clipboard.SetText(accessUrl);
        }
        catch (ExternalException)
        {
            MessageBox.Show(
                "Não foi possível copiar o endereço agora. Tente novamente.",
                "NFe Agendamento",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
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
