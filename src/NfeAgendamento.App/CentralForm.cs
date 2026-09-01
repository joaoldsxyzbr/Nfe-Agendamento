using System.Diagnostics;

namespace NfeAgendamento.App;

public sealed class CentralForm : Form
{
    public static IReadOnlyList<string> PrimaryActionLabels { get; } =
        ["Iniciar Central", "Parar Central", "Abrir sistema"];

    private readonly CentralStateService _centralState;
    private readonly WindowsFirewallService _firewall;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Label _statusValue;
    private readonly Label _ipValue;
    private readonly Label _portValue;
    private readonly Label _urlValue;
    private readonly Label _networkValue;
    private readonly Label _listenerValue;
    private readonly Label _firewallValue;
    private readonly Label _summaryValue;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Button _firewallButton;
    private bool _refreshing;

    public CentralForm(CentralStateService centralState, WindowsFirewallService? firewall = null)
    {
        _centralState = centralState ?? throw new ArgumentNullException(nameof(centralState));
        _firewall = firewall ?? new WindowsFirewallService();

        Text = "NFe Agendamento - Central";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(590, 500);
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
            Text = "Controle e diagnóstico do acesso dos outros computadores.",
            AutoSize = true,
            Location = new Point(31, 62)
        };

        _statusValue = CreateValueLabel(190, 106);
        _ipValue = CreateValueLabel(190, 140);
        _portValue = CreateValueLabel(190, 174);
        _urlValue = CreateValueLabel(190, 208);
        _urlValue.MaximumSize = new Size(360, 0);
        _networkValue = CreateValueLabel(190, 258);
        _listenerValue = CreateValueLabel(190, 292);
        _firewallValue = CreateValueLabel(190, 326);
        _summaryValue = new Label
        {
            AutoSize = false,
            Location = new Point(31, 366),
            Size = new Size(528, 48)
        };

        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(CreateCaption("Status", 31, 106));
        Controls.Add(CreateCaption("IP deste PC", 31, 140));
        Controls.Add(CreateCaption("Porta", 31, 174));
        Controls.Add(CreateCaption("Acesso pela rede", 31, 208));
        Controls.Add(CreateCaption("Rede", 31, 258));
        Controls.Add(CreateCaption("Servidor", 31, 292));
        Controls.Add(CreateCaption("Firewall", 31, 326));
        Controls.Add(_statusValue);
        Controls.Add(_ipValue);
        Controls.Add(_portValue);
        Controls.Add(_urlValue);
        Controls.Add(_networkValue);
        Controls.Add(_listenerValue);
        Controls.Add(_firewallValue);
        Controls.Add(_summaryValue);

        _startButton = new Button
        {
            Text = PrimaryActionLabels[0],
            Size = new Size(125, 38),
            Location = new Point(31, 438)
        };
        _startButton.Click += (_, _) =>
        {
            _centralState.SetEnabled(true);
            _ = RefreshDiagnosticsAsync();
        };

        _stopButton = new Button
        {
            Text = PrimaryActionLabels[1],
            Size = new Size(125, 38),
            Location = new Point(166, 438)
        };
        _stopButton.Click += (_, _) =>
        {
            _centralState.SetEnabled(false);
            _ = RefreshDiagnosticsAsync();
        };

        var openButton = new Button
        {
            Text = PrimaryActionLabels[2],
            Size = new Size(125, 38),
            Location = new Point(301, 438)
        };
        openButton.Click += (_, _) => OpenSystem();

        _firewallButton = new Button
        {
            Text = "Configurar firewall",
            Size = new Size(133, 38),
            Location = new Point(436, 438)
        };
        _firewallButton.Click += async (_, _) => await ConfigureFirewallAsync();

        Controls.Add(_startButton);
        Controls.Add(_stopButton);
        Controls.Add(openButton);
        Controls.Add(_firewallButton);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 10000 };
        _refreshTimer.Tick += async (_, _) => await RefreshDiagnosticsAsync();

        _centralState.Changed += CentralStateChanged;
        Shown += async (_, _) =>
        {
            _refreshTimer.Start();
            await RefreshDiagnosticsAsync();
        };
        FormClosed += (_, _) =>
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _centralState.Changed -= CentralStateChanged;
        };

        RefreshBasicStatus();
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
            BeginInvoke(new Action(() => _ = RefreshDiagnosticsAsync()));
            return;
        }

        _ = RefreshDiagnosticsAsync();
    }

    private void RefreshBasicStatus()
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

    private async Task RefreshDiagnosticsAsync()
    {
        if (_refreshing || IsDisposed)
            return;

        _refreshing = true;
        try
        {
            RefreshBasicStatus();
            var firewallStatus = _centralState.IsEnabled
                ? await _firewall.GetStatusAsync()
                : FirewallRuleStatus.Unavailable;
            var snapshot = CentralNetworkDiagnostics.Capture(_centralState.IsEnabled, firewallStatus);

            if (IsDisposed)
                return;

            _networkValue.Text = HealthText(snapshot.NetworkStatus);
            _listenerValue.Text = HealthText(snapshot.ListenerStatus);
            _firewallValue.Text = HealthText(snapshot.FirewallStatus);
            _summaryValue.Text = snapshot.Summary;
            _urlValue.Text = _centralState.IsEnabled ? snapshot.AccessUrl : "Acesso externo desativado";
            _firewallButton.Enabled = _centralState.IsEnabled && snapshot.FirewallStatus != NetworkHealthStatus.Ok;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task ConfigureFirewallAsync()
    {
        _firewallButton.Enabled = false;
        _summaryValue.Text = "Solicitando permissão do Windows para configurar a porta 17345...";

        var configured = await _firewall.EnsureRuleAsync();
        if (!configured)
        {
            MessageBox.Show(
                "O firewall não foi configurado. Autorize a solicitação do Windows ou peça apoio ao administrador da rede.",
                "NFe Agendamento",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        await RefreshDiagnosticsAsync();
    }

    private static string HealthText(NetworkHealthStatus status) => status switch
    {
        NetworkHealthStatus.Ok => "OK",
        NetworkHealthStatus.ActionRequired => "Precisa configurar",
        NetworkHealthStatus.Error => "Erro",
        NetworkHealthStatus.Inactive => "Inativo",
        _ => "Não verificado"
    };

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
