using System.Security.Cryptography.X509Certificates;
using System.Text;

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

        var persisted = $"{normalized}|{normalizedUf}";
        await WriteAtomicTextAsync(_selectionPath, persisted, cancellationToken);
        TryDelete(_authorityStatePath);
    }

    public Task<CertificateSelection?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = ReadPersistedSelection();
        if (state is null)
            return Task.FromResult<CertificateSelection?>(null);

        try
        {
            using var certificate = GetByThumbprint(state.Value.Thumbprint);
            return Task.FromResult<CertificateSelection?>(ToSelection(certificate));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult<CertificateSelection?>(null);
        }
    }

    public (X509Certificate2 Certificate, CertificateSelection Selection) GetCurrentSelectionWithCertificate()
    {
        var state = ReadPersistedSelection()
            ?? throw new InvalidOperationException("Nenhum certificado foi configurado.");

        var certificate = GetByThumbprint(state.Thumbprint);
        try
        {
            _ = CertificateIdentityReader.Read(certificate, state.UfAutor);
            return (new X509Certificate2(certificate), ToSelection(certificate));
        }
        finally
        {
            certificate.Dispose();
        }
    }

    public string? GetCurrentAuthorityState()
    {
        try
        {
            return ReadPersistedSelection()?.UfAutor;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private PersistedSelection? ReadPersistedSelection()
    {
        if (!File.Exists(_selectionPath))
            return null;

        var raw = File.ReadAllText(_selectionPath).Trim();
        if (raw.Length == 0)
            return null;

        var separator = raw.IndexOf('|');
        if (separator >= 0)
        {
            var thumbprint = raw[..separator].Trim();
            var ufAutor = raw[(separator + 1)..].Trim();
            try
            {
                return new PersistedSelection(
                    NormalizeThumbprint(thumbprint),
                    NormalizeAuthorityState(ufAutor));
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException("O estado salvo do certificado é inválido.", ex);
            }
        }

        var legacyUf = File.Exists(_authorityStatePath)
            ? File.ReadAllText(_authorityStatePath).Trim()
            : null;
        return new PersistedSelection(NormalizeThumbprint(raw), legacyUf);
    }

    private static async Task WriteAtomicTextAsync(
        string path,
        string value,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Caminho de estado local inválido.");
        Directory.CreateDirectory(directory);

        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

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

    private readonly record struct PersistedSelection(string Thumbprint, string? UfAutor);
}
