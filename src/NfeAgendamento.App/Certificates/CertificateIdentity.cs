using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace NfeAgendamento.App.Certificates;

public sealed record CertificateIdentity(string Cnpj, string UfAutor);

public static class CertificateIdentityReader
{
    private static readonly Regex CnpjRegex = new(@"(?<!\d)\d{14}(?!\d)", RegexOptions.Compiled);
    private static readonly Regex StateRegex = new(@"(?:^|[,;])\s*(?:S|ST|State|UF)\s*=\s*([A-Z]{2})(?:[,;]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly IReadOnlyDictionary<string, string> StateCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RO"] = "11", ["AC"] = "12", ["AM"] = "13", ["RR"] = "14", ["PA"] = "15", ["AP"] = "16", ["TO"] = "17",
        ["MA"] = "21", ["PI"] = "22", ["CE"] = "23", ["RN"] = "24", ["PB"] = "25", ["PE"] = "26", ["AL"] = "27", ["SE"] = "28", ["BA"] = "29",
        ["MG"] = "31", ["ES"] = "32", ["RJ"] = "33", ["SP"] = "35", ["PR"] = "41", ["SC"] = "42", ["RS"] = "43",
        ["MS"] = "50", ["MT"] = "51", ["GO"] = "52", ["DF"] = "53"
    };

    public static CertificateIdentity Read(X509Certificate2 certificate, string? ufAutor = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var subject = certificate.Subject ?? string.Empty;
        var cnpjMatches = CnpjRegex.Matches(subject).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (cnpjMatches.Length != 1)
            throw new InvalidOperationException("Não foi possível identificar com segurança o CNPJ no certificado selecionado.");

        var subjectState = StateRegex.Match(subject).Groups[1].Value;
        var state = string.IsNullOrWhiteSpace(ufAutor)
            ? StateCodes.GetValueOrDefault(subjectState, string.Empty)
            : ufAutor.Trim();
        if (state.Length != 2 || state.Any(c => c is < '0' or > '9'))
            throw new InvalidOperationException("Informe a UF autora em formato numérico (ex.: 42 para SC).");

        return new CertificateIdentity(cnpjMatches[0], state);
    }
}
