using System.Diagnostics;

namespace NfeAgendamento.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly string _listenUrl;

    public TrayApplicationContext(string listenUrl)
    {
        _listenUrl = listenUrl;

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Abrir sistema");
        open.Click += (_, _) => OpenSystem();
        var exit = new ToolStripMenuItem("Sair");
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(open);
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
}
