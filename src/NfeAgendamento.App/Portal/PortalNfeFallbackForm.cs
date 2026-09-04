using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NfeAgendamento.App.Certificates;
using NfeAgendamento.App.Storage;

namespace NfeAgendamento.App.Portal;

public sealed class PortalNfeFallbackForm : Form
{
    private const string OfficialHost = "www.nfe.fazenda.gov.br";
    private const string PortalUrl = "https://www.nfe.fazenda.gov.br/portal/consultaRecaptcha.aspx?tipoConsulta=resumo&tipoConteudo=7PhJ+gAVw2g%3D";
    private const long MaxXmlBytes = 10L * 1024 * 1024;

    private readonly string _accessKey;
    private readonly CertificateService _certificates;
    private readonly EncryptedXmlCache _cache;
    private readonly WebView2 _webView;
    private readonly Label _status;
    private string _selectedThumbprint = string.Empty;
    private string? _temporaryDownloadPath;
    private bool _downloadInProgress;

    public PortalNfeFallbackForm(
        string accessKey,
        CertificateService certificates,
        EncryptedXmlCache cache)
    {
        _accessKey = accessKey ?? throw new ArgumentNullException(nameof(accessKey));
        _certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        Text = "NFe Agendamento - Consulta pela Fazenda";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 650);
        ClientSize = new Size(1100, 800);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application;

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(16, 12, 16, 8),
            BackColor = Color.White
        };

        var title = new Label
        {
            AutoSize = true,
            Text = "Consulta alternativa no Portal Nacional da NF-e",
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point),
            Location = new Point(16, 10),
            ForeColor = Color.FromArgb(20, 52, 86),
            BackColor = Color.Transparent
        };

        _status = new Label
        {
            AutoEllipsis = true,
            Text = "A chave será preenchida automaticamente. Resolva o hCaptcha manualmente e clique em Consultar.",
            Location = new Point(17, 41),
            Size = new Size(980, 24),
            ForeColor = Color.FromArgb(65, 72, 82),
            BackColor = Color.Transparent
        };

        var closeButton = new Button
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Text = "Fechar",
            Size = new Size(80, 32),
            Location = new Point(1000, 22)
        };
        closeButton.Click += (_, _) => Close();

        header.Controls.Add(title);
        header.Controls.Add(_status);
        header.Controls.Add(closeButton);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.White
        };

        Controls.Add(_webView);
        Controls.Add(header);

        Shown += async (_, _) => await InitializeBrowserAsync();
        FormClosed += (_, _) => CleanupTemporaryDownload();
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            var current = _certificates.GetCurrentSelectionWithCertificate();
            try
            {
                _selectedThumbprint = NormalizeThumbprint(current.Selection.Thumbprint);
            }
            finally
            {
                current.Certificate.Dispose();
            }

            var userDataFolder = Path.Combine(AppPaths.LocalDataRoot, "webview2");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(environment);

            var core = _webView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = true;
            core.NavigationStarting += CoreNavigationStarting;
            core.NavigationCompleted += CoreNavigationCompleted;
            core.NewWindowRequested += CoreNewWindowRequested;
            core.ClientCertificateRequested += CoreClientCertificateRequested;
            core.DownloadStarting += CoreDownloadStarting;
            core.Navigate(PortalUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            SetStatus("O Microsoft Edge WebView2 Runtime não está instalado neste PC.", error: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    private void CoreNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsAllowedTopLevelUri(e.Uri))
            return;

        e.Cancel = true;
        SetStatus("A navegação externa foi bloqueada. Esta janela aceita somente o Portal Nacional da NF-e.", error: true);
    }

    private async void CoreNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _webView.CoreWebView2 is null || !IsOfficialPortalUri(_webView.Source?.AbsoluteUri))
            return;

        try
        {
            var script =
                "(() => {" +
                "const input = document.querySelector('#ctl00_ContentPlaceHolder1_txtChaveAcessoResumo, input[id$=\"txtChaveAcessoResumo\"]');" +
                "if (!input) return false;" +
                $"if (!input.value) input.value = '{_accessKey}';" +
                "input.dispatchEvent(new Event('input', { bubbles: true }));" +
                "input.dispatchEvent(new Event('change', { bubbles: true }));" +
                "input.focus();" +
                "return true;" +
                "})();";
            await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void CoreNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (IsOfficialPortalUri(e.Uri))
            _webView.CoreWebView2.Navigate(e.Uri);
        else
            SetStatus("Uma tentativa de abrir conteúdo externo foi bloqueada.", error: true);
    }

    private void CoreClientCertificateRequested(object? sender, CoreWebView2ClientCertificateRequestedEventArgs e)
    {
        if (!IsOfficialHost(e.Host))
        {
            e.Cancel = true;
            e.Handled = true;
            SetStatus("Uma solicitação de certificado fora do Portal Nacional foi bloqueada.", error: true);
            return;
        }

        CoreWebView2ClientCertificate? selected = null;
        foreach (var candidate in e.MutuallyTrustedCertificates)
        {
            try
            {
                using var certificate = candidate.ToX509Certificate2();
                if (string.Equals(
                    NormalizeThumbprint(certificate.Thumbprint),
                    _selectedThumbprint,
                    StringComparison.Ordinal))
                {
                    selected = candidate;
                    break;
                }
            }
            catch (CryptographicException)
            {
            }
        }

        e.Handled = true;
        if (selected is null)
        {
            SetStatus("O certificado configurado no NFe Agendamento não foi aceito pelo Portal para este download.", error: true);
            return;
        }

        e.SelectedCertificate = selected;
        SetStatus("Certificado configurado selecionado. Aguardando o XML da Fazenda...");
    }

    private void CoreDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        if (!IsOfficialXmlDownload(e.DownloadOperation.Uri))
        {
            e.Cancel = true;
            e.Handled = true;
            SetStatus("Um download que não corresponde ao XML oficial da NF-e foi bloqueado.", error: true);
            return;
        }

        if (_downloadInProgress)
        {
            e.Cancel = true;
            e.Handled = true;
            SetStatus("Já existe um download de XML em andamento nesta janela.", error: true);
            return;
        }

        _downloadInProgress = true;
        var directory = Path.Combine(Path.GetTempPath(), "NfeAgendamento", "portal-nfe");
        Directory.CreateDirectory(directory);
        _temporaryDownloadPath = Path.Combine(directory, $"{_accessKey}-{Guid.NewGuid():N}.xml");

        e.ResultFilePath = _temporaryDownloadPath;
        e.Handled = true;

        var operation = e.DownloadOperation;
        EventHandler<object>? stateChanged = null;
        stateChanged = async (_, _) =>
        {
            if (operation.State == CoreWebView2DownloadState.InProgress)
                return;

            if (stateChanged is not null)
                operation.StateChanged -= stateChanged;

            if (operation.State == CoreWebView2DownloadState.Completed)
            {
                await ImportDownloadedXmlAsync(_temporaryDownloadPath);
                return;
            }

            _downloadInProgress = false;
            CleanupTemporaryDownload();
            SetStatus($"O download do XML foi interrompido ({operation.InterruptReason}). Tente novamente pelo Portal.", error: true);
        };
        operation.StateChanged += stateChanged;
        SetStatus("Download oficial iniciado. Validando o XML...");
    }

    private async Task ImportDownloadedXmlAsync(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidDataException("O Portal não gerou um arquivo XML utilizável.");

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxXmlBytes)
                throw new InvalidDataException("O XML baixado pelo Portal possui tamanho inválido.");

            var xml = await File.ReadAllTextAsync(path);
            var validated = NfePortalXmlValidator.ValidateAndNormalize(xml, _accessKey);
            await _cache.PutAsync(_accessKey, validated);

            SetStatus("XML validado e salvo no cache. O site será atualizado automaticamente.");
            Close();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SetStatus(ex.Message, error: true);
            MessageBox.Show(
                this,
                ex.Message,
                "XML não importado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _downloadInProgress = false;
            CleanupTemporaryDownload();
        }
    }

    private void SetStatus(string message, bool error = false)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetStatus(message, error)));
            return;
        }

        _status.Text = message;
        _status.ForeColor = error ? Color.Firebrick : Color.FromArgb(65, 72, 82);
    }

    private void CleanupTemporaryDownload()
    {
        var path = _temporaryDownloadPath;
        _temporaryDownloadPath = null;
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsAllowedTopLevelUri(string? uri)
    {
        if (string.Equals(uri, "about:blank", StringComparison.OrdinalIgnoreCase))
            return true;
        return IsOfficialPortalUri(uri);
    }

    private static bool IsOfficialPortalUri(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && IsOfficialHost(parsed.Host);

    private static bool IsOfficialXmlDownload(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && IsOfficialHost(parsed.Host)
        && string.Equals(parsed.AbsolutePath, "/portal/downloadNFe.aspx", StringComparison.OrdinalIgnoreCase);

    private static bool IsOfficialHost(string? host) =>
        string.Equals(host, OfficialHost, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeThumbprint(string? thumbprint) =>
        string.Concat((thumbprint ?? string.Empty).Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
}
