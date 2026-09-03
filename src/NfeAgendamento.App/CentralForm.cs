using System.Diagnostics;
using NfeAgendamento.App.SharedQueue;

namespace NfeAgendamento.App;

public sealed class CentralForm : Form
{
    public static IReadOnlyList<string> PrimaryActionLabels { get; } = ["Abrir sistema"];

    private readonly SharedQueueCentralService _centralRuntime;
    private readonly SharedQueueClient _queueClient;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Label _roleValue;
    private readonly Label _shareValue;
    private readonly Label _centralValue;
    private readonly Label _heartbeatValue;
    private readonly Label _processorValue;
    private readonly Label _summaryValue;
    private bool _refreshing;

    public CentralForm(
        CentralStateService centralState,
        SharedQueueCentralService centralRuntime,
        SharedQueueClient queueClient)
    {
        ArgumentNullException.ThrowIfNull(centralState); // mantido apenas para compatibilidade da migração legada.
        _centralRuntime = centralRuntime ?? throw new ArgumentNullException(nameof(centralRuntime));
        _queueClient = queueClient ?? throw new ArgumentNullException(nameof(queueClient));

        Text = "NFe Agendamento - Fila";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(590, 445);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application;
        BackColor = CentralTheme.Background;
        ForeColor = CentralTheme.Text;

        var title = new Label
        {
            Text = "Fila NFe Agendamento",
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(28, 24),
            ForeColor = CentralTheme.BrandBlue,
            BackColor = Color.Transparent
        };

        var subtitle = new Label
        {
            Text = "A liderança é escolhida automaticamente entre os PCs autorizados.",
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
        Controls.Add(CreateCaption("Líder da fila", 31, 188));
        Controls.Add(CreateCaption("Heartbeat", 31, 226));
        Controls.Add(CreateCaption("Processador", 31, 264));
        Controls.Add(_roleValue);
        Controls.Add(_shareValue);
        Controls.Add(_centralValue);
        Controls.Add(_heartbeatValue);
        Controls.Add(_processorValue);
        Controls.Add(_summaryValue);

        var openButton = new Button
        {
            Text = "Abrir sistema",
            Size = new Size(528, 38),
            Location = new Point(31, 385)
        };
        StylePrimaryButton(openButton, CentralTheme.BrandBlue, Color.White);
        openButton.Click += (_, _) => OpenSystem();
        Controls.Add(openButton);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _refreshTimer.Tick += (_, _) => RefreshDiagnostics();
        Shown += (_, _) =>
        {
            _refreshTimer.Start();
            RefreshDiagnostics();
        };
        FormClosed += (_, _) =>
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
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

    private void RefreshDiagnostics()
    {
        if (_refreshing || IsDisposed)
            return;

        _refreshing = true;
        try
        {
            var clientStatus = _queueClient.GetStatus();
            var shareAvailable = _centralRuntime.ShareAvailable || clientStatus.ShareAvailable;
            _shareValue.Text = shareAvailable
                ? $"OK — {SharedQueuePaths.DefaultRoot}"
                : $"Indisponível — {SharedQueuePaths.DefaultRoot}";
            _shareValue.ForeColor = shareAvailable ? CentralTheme.Success : CentralTheme.Danger;

            switch (_centralRuntime.Status)
            {
                case CentralRuntimeStatus.Active:
                    _roleValue.Text = "Líder automático";
                    _roleValue.ForeColor = CentralTheme.BrandBlue;
                    _centralValue.Text = $"Este PC — {Environment.MachineName}";
                    _centralValue.ForeColor = CentralTheme.Success;
                    _processorValue.Text = "Ativo";
                    _processorValue.ForeColor = CentralTheme.Success;
                    _summaryValue.Text = "Este PC está processando a fila. Se ele sair, outro PC autorizado pode assumir automaticamente.";
                    _summaryValue.ForeColor = CentralTheme.Success;
                    SetHeartbeat(_centralRuntime.LastHeartbeatUtc);
                    break;

                case CentralRuntimeStatus.Standby:
                    _roleValue.Text = "Candidato em espera";
                    _roleValue.ForeColor = CentralTheme.Text;
                    _centralValue.Text = string.IsNullOrWhiteSpace(clientStatus.CentralId)
                        ? "Outro PC está processando"
                        : clientStatus.CentralId;
                    _centralValue.ForeColor = clientStatus.CentralOnline ? CentralTheme.Success : CentralTheme.Warning;
                    _processorValue.Text = "Standby";
                    _processorValue.ForeColor = CentralTheme.MutedText;
                    _summaryValue.Text = "Outro PC possui o lock da fila. Este PC assumirá automaticamente se o líder sair.";
                    _summaryValue.ForeColor = CentralTheme.MutedText;
                    SetHeartbeat(clientStatus.LastHeartbeatUtc);
                    break;

                case CentralRuntimeStatus.ShareUnavailable:
                    _roleValue.Text = "Aguardando pasta";
                    _roleValue.ForeColor = CentralTheme.Danger;
                    _centralValue.Text = "Indisponível";
                    _centralValue.ForeColor = CentralTheme.Danger;
                    _processorValue.Text = "Parado";
                    _processorValue.ForeColor = CentralTheme.Danger;
                    _summaryValue.Text = $"Não foi possível usar {SharedQueuePaths.DefaultRoot}. Nenhuma consulta fiscal será iniciada sem a pasta compartilhada.";
                    _summaryValue.ForeColor = CentralTheme.Danger;
                    SetHeartbeat(null);
                    break;

                default:
                    _roleValue.Text = clientStatus.IsPaired ? "Candidato" : "Não autorizado";
                    _roleValue.ForeColor = clientStatus.IsPaired ? CentralTheme.Text : CentralTheme.Warning;
                    _centralValue.Text = clientStatus.CentralOnline
                        ? clientStatus.CentralId ?? "Líder online"
                        : "Aguardando líder";
                    _centralValue.ForeColor = clientStatus.CentralOnline ? CentralTheme.Success : CentralTheme.Warning;
                    _processorValue.Text = clientStatus.IsPaired ? "Aguardando eleição" : "Indisponível";
                    _processorValue.ForeColor = CentralTheme.MutedText;
                    _summaryValue.Text = clientStatus.Message ?? "Este PC ainda está entrando no grupo da fila compartilhada.";
                    _summaryValue.ForeColor = CentralTheme.MutedText;
                    SetHeartbeat(clientStatus.LastHeartbeatUtc);
                    break;
            }
        }
        finally
        {
            _refreshing = false;
        }
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
