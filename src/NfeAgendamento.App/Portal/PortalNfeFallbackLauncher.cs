using Microsoft.Web.WebView2.Core;
using NfeAgendamento.App.Certificates;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.Storage;

namespace NfeAgendamento.App.Portal;

public enum PortalFallbackLaunchStatus
{
    Started,
    Busy,
    RuntimeMissing,
    ConfigurationError
}

public sealed record PortalFallbackLaunchResult(PortalFallbackLaunchStatus Status, string Message);

public sealed class PortalNfeFallbackLauncher
{
    private readonly CertificateService _certificates;
    private readonly EncryptedXmlCache _cache;
    private int _windowOpen;

    public PortalNfeFallbackLauncher(CertificateService certificates, EncryptedXmlCache cache)
    {
        _certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public PortalFallbackLaunchResult TryLaunch(string accessKey)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));

        if (Interlocked.CompareExchange(ref _windowOpen, 1, 0) != 0)
        {
            return new PortalFallbackLaunchResult(
                PortalFallbackLaunchStatus.Busy,
                "Já existe uma consulta alternativa aberta neste PC líder.");
        }

        try
        {
            var current = _certificates.GetCurrentSelectionWithCertificate();
            current.Certificate.Dispose();

            var runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(runtimeVersion))
            {
                Volatile.Write(ref _windowOpen, 0);
                return new PortalFallbackLaunchResult(
                    PortalFallbackLaunchStatus.RuntimeMissing,
                    "O Microsoft Edge WebView2 Runtime não está disponível neste PC.");
            }

            var thread = new Thread(() => RunWindow(accessKey))
            {
                IsBackground = true,
                Name = "NfeAgendamento.PortalNfeFallback"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return new PortalFallbackLaunchResult(
                PortalFallbackLaunchStatus.Started,
                "A consulta alternativa foi aberta neste PC líder.");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Volatile.Write(ref _windowOpen, 0);
            return new PortalFallbackLaunchResult(
                PortalFallbackLaunchStatus.RuntimeMissing,
                "O Microsoft Edge WebView2 Runtime não está instalado neste PC.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Volatile.Write(ref _windowOpen, 0);
            return new PortalFallbackLaunchResult(
                PortalFallbackLaunchStatus.ConfigurationError,
                ex.Message);
        }
    }

    private void RunWindow(string accessKey)
    {
        try
        {
            using var form = new PortalNfeFallbackForm(accessKey, _certificates, _cache);
            Application.Run(form);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível abrir a consulta alternativa da NF-e.\n\n{ex.Message}",
                "NFe Agendamento",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            Volatile.Write(ref _windowOpen, 0);
        }
    }
}
