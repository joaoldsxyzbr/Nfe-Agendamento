using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace NfeAgendamento.App.Certificates;

public sealed record CertificateIdentity(string Cnpj, string UfAutor);

public static class CertificateIdentityReader
{
    private static readonly Regex CnpjRegex = new(@"(?<!\d)\d{14}(?!\d)", RegexOptions.Compiled);
    private static readonly Regex StateRegex = new(@"(?:^|[,;])\s*(?:S|ST|State|UF)\s*=\s*([A-Z]{2})(?:[,;]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static CertificateIdentity Read(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var subject = certificate.Subject ?? string.Empty;
        var cnpjMatches = CnpjRegex.Matches(subject).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (cnpjMatches.Length != 1)
            throw new InvalidOperationException("Não foi possível identificar com segurança o CNPJ no certificado selecionado.");

        var stateMatch = StateRegex.Match(subject);
        if (!stateMatch.Success)
            throw new InvalidOperationException("Não foi possível identificar com segurança a UF no certificado selecionado.");

        return new CertificateIdentity(cnpjMatches[0], stateMatch.Groups[1].Value.ToUpperInvariant());
    }
}
