using System.Diagnostics;
using NfeAgendamento.App.SharedQueue;

namespace NfeAgendamento.App;

public sealed class CentralForm : Form
{
    public static IReadOnlyList<string> PrimaryActionLabels { get; } =
        ["Iniciar Central", "Parar Central", "Abrir sistema"];

    private readonly CentralStateService _centralState;
    private readonly SharedQueueCentralService _centralRuntime;
    private readonly SharedQueueClient _queueClient;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Label _roleValue;
    private readonly Label _shareValue;
    private readonly Label _centralValue;
    private readonly Label _heartbeatValue;
    private readonly Label _processorValue;
    private readonly Label _summaryValue;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private bool _refreshing;

    public CentralForm(
        CentralStateService centralState,
        SharedQueueCentralService centralRuntime,
        SharedQueueClient queueClient)
    {
        _centralState = centralState ?? throw new ArgumentNullException(nameof(centralState));
        _centralRuntime = centralRuntime ?? throw new ArgumentNullException(nameof(centralRuntime));
        _queueClient = queueClient ?? throw new ArgumentNullException(nameof(queueClient));

        Text = "NFe Agendamento - Central";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(590, 465);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application;
        BackColor = CentralTheme.Background;
        ForeColor = CentralTheme.Text;

        var title = new Label
        {
            Text = "Central NFe Agendamento",
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(28, 24),
            ForeColor = CentralTheme.BrandBlue,
            BackColor = Color.Transparent
        };

        var subtitle = new Label
        {
            Text = "Comunicação segura pela pasta compartilhada da empresa.",
            AutoSize = true,
            Location = new Point(31, 62),
            ForeColor = CentralTheme.MutedText,
            BackColor = Color.Transparent
        };

        var brandAccent = new Panel
        {
            Location = new Point(31, 88),
            Size = new Size(528, 4),
            BackColor = CentralTheme.BrandYellow
        };

        _roleValue = CreateValueLabel(210, 112);
        _shareValue = CreateValueLabel(210, 150);
        _shareValue.MaximumSize = new Size(345, 0);
        _centralValue = CreateValueLabel(210, 188);
        _heartbeatValue = CreateValueLabel(210, 226);
        _processorValue = CreateValueLabel(210, 264);
        _summaryValue = new Label
        {
            AutoSize = false,
            Location = new Point(31, 312),
            Size = new Size(528, 55),
            ForeColor = CentralTheme.MutedText,
            BackColor = Color.Transparent
        };

        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(brandAccent);
        Controls.Add(CreateCaption("Papel deste PC", 31, 112));
        Controls.Add(CreateCaption("Pasta compartilhada", 31, 150));
        Controls.Add(CreateCaption("Central", 31, 188));
        Controls.Add(CreateCaption("Heartbeat", 31, 226));
        Controls.Add(CreateCaption("Processador", 31, 264));
        Controls.Add(_roleValue);
        Controls.Add(_shareValue);
        Controls.Add(_centralValue);
        Controls.Add(_heartbeatValue);
        Controls.Add(_processorValue);
        Controls.Add(_summaryValue);

        _startButton = new Button
        {
            Text = "Iniciar Central",
            Size = new Size(155, 38),
            Location = new Point(31, 395)
        };
        StylePrimaryButton(_startButton, CentralTheme.BrandBlue, Color.White);
        _startButton.Click += (_, _) =>
        {
            _centralState.SetConfiguredAsCentral(true);
            RefreshDiagnostics();
        };

        _stopButton = new Button
        {
            Text = "Parar Central",
            Size = new Size(155, 38),
            Location = new Point(201, 395)
        };
        StyleOutlineButton(_stopButton);
        _stopButton.Click += (_, _) =>
        {
            _centralState.SetConfiguredAsCentral(false);
            RefreshDiagnostics();
        };

        var openButton = new Button
        {
            Text = "Abrir sistema",
            Size = new Size(155, 38),
            Location = new Point(371, 395)
        };
        StylePrimaryButton(openButton, CentralTheme.BrandBlueSoft, Color.White);
        openButton.Click += (_, _) => OpenSystem();

        Controls.Add(_startButton);
        Controls.Add(_stopButton);
        Controls.Add(openButton);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _refreshTimer.Tick += (_, _) => RefreshDiagnostics();

        _centralState.Changed += CentralStateChanged;
        Shown += (_, _) =>
        {
            _refreshTimer.Start();
            RefreshDiagnostics();
        };
        FormClosed += (_, _) =>
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _centralState.Changed -= CentralStateChanged;
        };

        RefreshDiagnostics();
    }

    private static Label CreateCaption(string text, int x, int y) => new()
    {
        Text = text + ":",
        AutoSize = true,
        Location = new Point(x, y),
        ForeColor = CentralTheme.MutedText,
        BackColor = Color.Transparent
    };

    private static Label CreateValueLabel(int x, int y) => new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
        Location = new Point(x, y),
        ForeColor = CentralTheme.Text,
        BackColor = Color.Transparent
    };

    private static void StylePrimaryButton(Button button, Color backColor, Color foreColor)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    private static void StyleOutlineButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = CentralTheme.BrandBlue;
        button.BackColor = CentralTheme.Surface;
        button.ForeColor = CentralTheme.BrandBlue;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    private void CentralStateChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(RefreshDiagnostics));
            return;
        }

        RefreshDiagnostics();
    }

    private void RefreshDiagnostics()
    {
        if (_refreshing || IsDisposed)
            return;

        _refreshing = true;
        try
        {
            var configuredAsCentral = _centralState.IsConfiguredAsCentral;
            var clientStatus = _queueClient.GetStatus();

            _roleValue.Text = configuredAsCentral ? "Central configurada" : "Cliente";
            _roleValue.ForeColor = configuredAsCentral ? CentralTheme.BrandBlue : CentralTheme.Text;

            var shareAvailable = configuredAsCentral ? _centralRuntime.ShareAvailable : clientStatus.ShareAvailable;
            _shareValue.Text = shareAvailable
                ? $"OK — {SharedQueuePaths.DefaultRoot}"
                : $"Indisponível — {SharedQueuePaths.DefaultRoot}";
            _shareValue.ForeColor = shareAvailable ? CentralTheme.Success : CentralTheme.Danger;

            if (configuredAsCentral)
                RefreshConfiguredCentral();
            else
                RefreshClient(clientStatus);

            _startButton.Enabled = !configuredAsCentral;
            _stopButton.Enabled = configuredAsCentral;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshConfiguredCentral()
    {
        switch (_centralRuntime.Status)
        {
            case CentralRuntimeStatus.Active:
                _centralValue.Text = "Central ativa";
                _centralValue.ForeColor = CentralTheme.Success;
                _processorValue.Text = "Ativo";
                _processorValue.ForeColor = CentralTheme.Success;
                _summaryValue.Text = "Este PC está processando as consultas enviadas pelos demais computadores.";
                _summaryValue.ForeColor = CentralTheme.Success;
                break;

            case CentralRuntimeStatus.Conflict:
                _centralValue.Text = "Conflito: outra Central ativa";
                _centralValue.ForeColor = CentralTheme.Warning;
                _processorValue.Text = "Parado";
                _processorValue.ForeColor = CentralTheme.Warning;
                _summaryValue.Text = "Outro computador já assumiu a Central. Pare a Central neste PC ou encerre a outra instância antes de tentar novamente.";
                _summaryValue.ForeColor = CentralTheme.Warning;
                break;

            case CentralRuntimeStatus.ShareUnavailable:
                _centralValue.Text = "Central aguardando pasta";
                _centralValue.ForeColor = CentralTheme.Danger;
                _processorValue.Text = "Aguardando pasta";
                _processorValue.ForeColor = CentralTheme.Danger;
                _summaryValue.Text = $"Não foi possível usar {SharedQueuePaths.DefaultRoot}. O aplicativo não tentará abrir portas ou alterar o firewall.";
                _summaryValue.ForeColor = CentralTheme.Danger;
                break;

            default:
                _centralValue.Text = "Iniciando Central...";
                _centralValue.ForeColor = CentralTheme.Warning;
                _processorValue.Text = "Aguardando";
                _processorValue.ForeColor = CentralTheme.MutedText;
                _summaryValue.Text = "Aguardando a Central assumir o lock da pasta compartilhada.";
                _summaryValue.ForeColor = CentralTheme.MutedText;
                break;
        }

        SetHeartbeat(_centralRuntime.LastHeartbeatUtc);
    }

    private void RefreshClient(SharedQueueClientStatus status)
    {
        if (status.CentralOnline)
        {
            _centralValue.Text = string.IsNullOrWhiteSpace(status.CentralId)
                ? "Central online"
                : $"Central online — {status.CentralId}";
            _centralValue.ForeColor = CentralTheme.Success;
            _processorValue.Text = "Remoto";
            _processorValue.ForeColor = CentralTheme.Success;
            _summaryValue.Text = "Este PC é cliente. As consultas serão enviadas de forma criptografada pela pasta compartilhada.";
            _summaryValue.ForeColor = CentralTheme.Success;
        }
        else
        {
            _centralValue.Text = "Central offline";
            _centralValue.ForeColor = CentralTheme.Danger;
            _processorValue.Text = "Indisponível";
            _processorValue.ForeColor = CentralTheme.MutedText;
            _summaryValue.Text = status.Message ?? "A Central não está disponível no momento.";
            _summaryValue.ForeColor = CentralTheme.Danger;
        }

        SetHeartbeat(status.LastHeartbeatUtc);
    }

    private void SetHeartbeat(DateTimeOffset? heartbeatUtc)
    {
        if (heartbeatUtc is null)
        {
            _heartbeatValue.Text = "Sem heartbeat";
            _heartbeatValue.ForeColor = CentralTheme.MutedText;
            return;
        }

        var seconds = Math.Max(0, (int)Math.Round((DateTimeOffset.UtcNow - heartbeatUtc.Value).TotalSeconds));
        _heartbeatValue.Text = seconds <= 2 ? "Agora" : $"Há {seconds} s";
        _heartbeatValue.ForeColor = seconds <= 10 ? CentralTheme.Success : CentralTheme.Warning;
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
