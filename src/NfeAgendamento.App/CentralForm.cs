using System.Diagnostics;
using NfeAgendamento.App.SharedQueue;

namespace NfeAgendamento.App;

public sealed class CentralForm : Form
{
    public static IReadOnlyList<string> PrimaryActionLabels { get; } = ["Abrir sistema"];

    private readonly SharedQueuePaths _paths;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Label _shareValue;
    private readonly Label _shareStateValue;
    private readonly Label _summaryValue;

    public CentralForm(SharedQueuePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));

        Text = "NFe Agendamento - Fila";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(590, 405);
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
            Text = "Coordenação por pasta compartilhada, sem Central e sem pareamento.",
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

        var modeValue = CreateValueLabel(210, 112);
        modeValue.Text = "Coordenação por pasta compartilhada";
        modeValue.ForeColor = CentralTheme.BrandBlue;

        var computerValue = CreateValueLabel(210, 150);
        computerValue.Text = Environment.MachineName;

        _shareValue = CreateValueLabel(210, 188);
        _shareValue.MaximumSize = new Size(345, 0);
        _shareStateValue = CreateValueLabel(210, 226);

        var lockValue = CreateValueLabel(210, 264);
        lockValue.Text = "Lock fiscal exclusivo durante cada consulta";

        _summaryValue = new Label
        {
            AutoSize = false,
            Location = new Point(31, 306),
            Size = new Size(528, 44),
            ForeColor = CentralTheme.MutedText,
            BackColor = Color.Transparent
        };

        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(brandAccent);
        Controls.Add(CreateCaption("Modo", 31, 112));
        Controls.Add(CreateCaption("PC local", 31, 150));
        Controls.Add(CreateCaption("Pasta compartilhada", 31, 188));
        Controls.Add(CreateCaption("Disponibilidade", 31, 226));
        Controls.Add(CreateCaption("Lock fiscal", 31, 264));
        Controls.Add(modeValue);
        Controls.Add(computerValue);
        Controls.Add(_shareValue);
        Controls.Add(_shareStateValue);
        Controls.Add(lockValue);
        Controls.Add(_summaryValue);

        var openButton = new Button
        {
            Text = "Abrir sistema",
            Size = new Size(528, 38),
            Location = new Point(31, 354)
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
        if (IsDisposed)
            return;

        var available = _paths.ValidateForClient();
        _shareValue.Text = _paths.Root;
        _shareValue.ForeColor = available ? CentralTheme.Text : CentralTheme.Warning;
        _shareStateValue.Text = available ? "Disponível" : "Indisponível";
        _shareStateValue.ForeColor = available ? CentralTheme.Success : CentralTheme.Danger;

        if (available)
        {
            _summaryValue.Text = "Pronto. Este PC usa seu certificado A1 local e disputa o lock apenas ao iniciar uma operação fiscal.";
            _summaryValue.ForeColor = CentralTheme.Success;
        }
        else
        {
            _summaryValue.Text = "Nenhuma nova consulta será enviada à SEFAZ enquanto a pasta compartilhada estiver indisponível.";
            _summaryValue.ForeColor = CentralTheme.Danger;
        }
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
