using System.Diagnostics;

namespace NfeAgendamento.App;

public sealed class CentralForm : Form
{
    public static IReadOnlyList<string> PrimaryActionLabels { get; } =
        ["Iniciar Central", "Parar Central", "Abrir sistema"];

    private readonly CentralStateService _centralState;
    private readonly Label _statusValue;
    private readonly Label _ipValue;
    private readonly Label _portValue;
    private readonly Label _urlValue;
    private readonly Button _startButton;
    private readonly Button _stopButton;

    public CentralForm(CentralStateService centralState)
    {
        _centralState = centralState ?? throw new ArgumentNullException(nameof(centralState));

        Text = "NFe Agendamento - Central";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(520, 320);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application;

        var title = new Label
        {
            Text = "Central NFe Agendamento",
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(28, 24)
        };

        var subtitle = new Label
        {
            Text = "Controle o acesso dos outros computadores ao sistema.",
            AutoSize = true,
            Location = new Point(31, 62)
        };

        _statusValue = CreateValueLabel(176, 106);
        _ipValue = CreateValueLabel(176, 142);
        _portValue = CreateValueLabel(176, 178);
        _urlValue = CreateValueLabel(176, 214);
        _urlValue.MaximumSize = new Size(310, 0);

        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(CreateCaption("Status", 31, 106));
        Controls.Add(CreateCaption("IP deste PC", 31, 142));
        Controls.Add(CreateCaption("Porta", 31, 178));
        Controls.Add(CreateCaption("Acesso pela rede", 31, 214));
        Controls.Add(_statusValue);
        Controls.Add(_ipValue);
        Controls.Add(_portValue);
        Controls.Add(_urlValue);

        _startButton = new Button
        {
            Text = PrimaryActionLabels[0],
            Size = new Size(135, 38),
            Location = new Point(31, 260)
        };
        _startButton.Click += (_, _) => _centralState.SetEnabled(true);

        _stopButton = new Button
        {
            Text = PrimaryActionLabels[1],
            Size = new Size(135, 38),
            Location = new Point(176, 260)
        };
        _stopButton.Click += (_, _) => _centralState.SetEnabled(false);

        var openButton = new Button
        {
            Text = PrimaryActionLabels[2],
            Size = new Size(150, 38),
            Location = new Point(321, 260)
        };
        openButton.Click += (_, _) => OpenSystem();

        Controls.Add(_startButton);
        Controls.Add(_stopButton);
        Controls.Add(openButton);

        _centralState.Changed += CentralStateChanged;
        FormClosed += (_, _) => _centralState.Changed -= CentralStateChanged;
        RefreshStatus();
    }

    private static Label CreateCaption(string text, int x, int y) => new()
    {
        Text = text + ":",
        AutoSize = true,
        Location = new Point(x, y)
    };

    private static Label CreateValueLabel(int x, int y) => new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
        Location = new Point(x, y)
    };

    private void CentralStateChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(RefreshStatus);
            return;
        }

        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var enabled = _centralState.IsEnabled;
        var address = CentralNetworkInfo.FindLanIPv4();

        _statusValue.Text = enabled ? "Central ativa" : "Central parada";
        _ipValue.Text = address?.ToString() ?? "Não identificado";
        _portValue.Text = LocalHost.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _urlValue.Text = enabled && address is not null
            ? CentralNetworkInfo.BuildAccessUrl(address)
            : "Acesso externo desativado";

        _startButton.Enabled = !enabled;
        _stopButton.Enabled = enabled;
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
}
