using System.Security.Cryptography.X509Certificates;

namespace NfeAgendamento.App.Certificates;

public sealed class CertificateService
{
    private readonly string _selectionPath;

    public CertificateService()
        : this(Path.Combine(AppPaths.StateRoot, "certificate-thumbprint.txt"))
    {
    }

    public CertificateService(string selectionPath)
    {
        if (string.IsNullOrWhiteSpace(selectionPath))
            throw new ArgumentException("Caminho de seleção de certificado inválido.", nameof(selectionPath));

        _selectionPath = selectionPath;
    }

    public IReadOnlyList<CertificateSelection> ListValidCertificates()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

        var usable = FilterUsable(store.Certificates.Cast<X509Certificate2>(), DateTimeOffset.UtcNow);
        return usable
            .Select(ToSelection)
            .OrderBy(item => item.Subject, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public X509Certificate2 GetByThumbprint(string thumbprint)
    {
        var normalized = NormalizeThumbprint(thumbprint);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, validOnly: false);
        var certificate = FilterUsable(matches.Cast<X509Certificate2>(), DateTimeOffset.UtcNow)
            .FirstOrDefault();

        return certificate ?? throw new InvalidOperationException(
            "O certificado selecionado não foi encontrado, está vencido ou não possui chave privada disponível.");
    }

    public async Task SelectAsync(string thumbprint, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeThumbprint(thumbprint);
        using var certificate = GetByThumbprint(normalized);

        var directory = Path.GetDirectoryName(_selectionPath)
            ?? throw new InvalidOperationException("Caminho de estado local inválido.");
        Directory.CreateDirectory(directory);

        var temporary = _selectionPath + ".tmp";
        await File.WriteAllTextAsync(temporary, normalized, cancellationToken);
        File.Move(temporary, _selectionPath, overwrite: true);
    }

    public async Task<CertificateSelection?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_selectionPath))
            return null;

        var thumbprint = (await File.ReadAllTextAsync(_selectionPath, cancellationToken)).Trim();
        if (thumbprint.Length == 0)
            return null;

        try
        {
            using var certificate = GetByThumbprint(thumbprint);
            return ToSelection(certificate);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static IReadOnlyList<X509Certificate2> FilterUsable(
        IEnumerable<X509Certificate2> certificates,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(certificates);

        var instant = now.UtcDateTime;
        return certificates
            .Where(certificate =>
                certificate.HasPrivateKey
                && certificate.NotBefore.ToUniversalTime() <= instant
                && instant < certificate.NotAfter.ToUniversalTime())
            .ToArray();
    }

    public static CertificateSelection ToSelection(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return new CertificateSelection(
            NormalizeThumbprint(certificate.Thumbprint),
            certificate.Subject,
            certificate.NotAfter);
    }

    private static string NormalizeThumbprint(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            throw new ArgumentException("Thumbprint do certificado não informado.", nameof(thumbprint));

        return string.Concat(thumbprint.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
    }
}
