using System.Diagnostics;

namespace NfeAgendamento.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    public static IReadOnlyList<string> MenuLabels { get; } =
        ["Status da fila", "Abrir sistema", "Configurar certificado", "Verificar atualização", "Iniciar com o Windows", "Sair"];

    private readonly NotifyIcon _trayIcon;
    private readonly CentralForm _centralForm;
    private bool _allowClose;

    public TrayApplicationContext(
        CentralStateService centralState,
        SharedQueue.SharedQueueCentralService centralRuntime,
        SharedQueue.SharedQueueClient queueClient)
    {
        ArgumentNullException.ThrowIfNull(centralState);
        ArgumentNullException.ThrowIfNull(centralRuntime);
        ArgumentNullException.ThrowIfNull(queueClient);

        _centralForm = new CentralForm(centralState, centralRuntime, queueClient);
        _centralForm.FormClosing += CentralFormClosing;

        var menu = new ContextMenuStrip();
        var openCentral = new ToolStripMenuItem("Status da fila");
        openCentral.Click += (_, _) => OpenCentral();
        var open = new ToolStripMenuItem("Abrir sistema");
        open.Click += (_, _) => OpenSystem();
        var configureCertificate = new ToolStripMenuItem("Configurar certificado")
        {
            ToolTipText = "Configurar o certificado A1 deste PC"
        };
        configureCertificate.Click += (_, _) => OpenSystem();
        var checkUpdates = new ToolStripMenuItem("Verificar atualização");
        checkUpdates.Click += async (_, _) => await CheckForUpdatesAsync();
        var startup = new ToolStripMenuItem("Iniciar com o Windows")
        {
            CheckOnClick = false,
            Checked = StartupManager.IsEnabled()
        };
        startup.Click += (_, _) =>
        {
            var desired = !startup.Checked;
            try
            {
                StartupManager.SetEnabled(desired);
                startup.Checked = desired;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException)
            {
                MessageBox.Show(
                    $"Não foi possível alterar a inicialização automática neste PC.\n\n{ex.Message}",
                    "NFe Agendamento",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };
        var exit = new ToolStripMenuItem("Sair");
        exit.Click += (_, _) => ExitApplication();
        menu.Items.Add(openCentral);
        menu.Items.Add(open);
        menu.Items.Add(configureCertificate);
        menu.Items.Add(checkUpdates);
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _trayIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application,
            Text = "NFe Agendamento",
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

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            using var service = new Updates.UpdateService();
            var result = await service.CheckAsync();

            if (!result.IsUpdateAvailable)
            {
                MessageBox.Show(
                    "Você já está usando a versão mais recente disponível.",
                    "NFe Agendamento",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!result.CanInstall)
            {
                MessageBox.Show(
                    $"A versão {result.LatestVersion} foi encontrada, mas o pacote oficial não pôde ser validado para instalação automática. Faça a atualização manual pela release oficial do projeto.",
                    "NFe Agendamento",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var confirmation = MessageBox.Show(
                $"A versão {result.LatestVersion} está disponível.\n\nDeseja baixar, verificar, instalar e reiniciar o NFe Agendamento agora?",
                "Atualização disponível",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
            if (confirmation != DialogResult.Yes)
                return;

            var prepared = await service.PrepareUpdateAsync(
                result,
                AppContext.BaseDirectory,
                Environment.ProcessId);

            Updates.UpdateService.LaunchPreparedUpdate(prepared);
            ExitApplication();
        }
        catch (Exception ex) when (ex is HttpRequestException
            or InvalidDataException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"Não foi possível concluir a atualização agora.\n\n{ex.Message}",
                "NFe Agendamento",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
