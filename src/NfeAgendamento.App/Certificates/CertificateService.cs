using System.Security.Cryptography.X509Certificates;

namespace NfeAgendamento.App.Certificates;

public sealed class CertificateService
{
    private readonly string _selectionPath;
    private readonly string _authorityStatePath;

    public CertificateService()
        : this(Path.Combine(AppPaths.StateRoot, "certificate-thumbprint.txt"))
    {
    }

    public CertificateService(string selectionPath)
    {
        if (string.IsNullOrWhiteSpace(selectionPath))
            throw new ArgumentException("Caminho de seleção de certificado inválido.", nameof(selectionPath));

        _selectionPath = selectionPath;
        _authorityStatePath = selectionPath + ".uf";
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

    public async Task SelectAsync(string thumbprint, string ufAutor, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeThumbprint(thumbprint);
        var normalizedUf = NormalizeAuthorityState(ufAutor);
        using var certificate = GetByThumbprint(normalized);

        _ = CertificateIdentityReader.Read(certificate, normalizedUf);

        var directory = Path.GetDirectoryName(_selectionPath)
            ?? throw new InvalidOperationException("Caminho de estado local inválido.");
        Directory.CreateDirectory(directory);

        var temporary = _selectionPath + ".tmp";
        await File.WriteAllTextAsync(temporary, normalized, cancellationToken);
        File.Move(temporary, _selectionPath, overwrite: true);
        await File.WriteAllTextAsync(_authorityStatePath + ".tmp", normalizedUf, cancellationToken);
        File.Move(_authorityStatePath + ".tmp", _authorityStatePath, overwrite: true);
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

    public (X509Certificate2 Certificate, CertificateSelection Selection) GetCurrentSelectionWithCertificate()
    {
        if (!File.Exists(_selectionPath))
            throw new InvalidOperationException("Nenhum certificado foi configurado.");

        var thumbprint = File.ReadAllText(_selectionPath).Trim();
        if (thumbprint.Length == 0)
            throw new InvalidOperationException("Nenhum certificado foi configurado.");

        var certificate = GetByThumbprint(thumbprint);
        try
        {
            var ufAutor = GetCurrentAuthorityState();
            _ = CertificateIdentityReader.Read(certificate, ufAutor);
            return (new X509Certificate2(certificate), ToSelection(certificate));
        }
        finally
        {
            certificate.Dispose();
        }
    }

    public string? GetCurrentAuthorityState() =>
        File.Exists(_authorityStatePath) ? File.ReadAllText(_authorityStatePath).Trim() : null;

    private static string NormalizeAuthorityState(string ufAutor)
    {
        if (string.IsNullOrWhiteSpace(ufAutor) || ufAutor.Length != 2 || ufAutor.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("Informe a UF autora em formato numérico (ex.: 42 para SC).", nameof(ufAutor));
        return ufAutor;
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
