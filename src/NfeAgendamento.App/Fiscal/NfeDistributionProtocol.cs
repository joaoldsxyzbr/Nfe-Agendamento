using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace NfeAgendamento.App.Fiscal;

public static class NfeDistributionProtocol
{
    private const int MaxXmlBytes = 10 * 1024 * 1024;

    public static string BuildSoap(string accessKey, string cnpj, string ufAutor)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));
        if (cnpj.Length != 14 || cnpj.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("CNPJ inválido.", nameof(cnpj));
        if (ufAutor.Length != 2 || ufAutor.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("UF autora inválida.", nameof(ufAutor));

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <soap:Body>
                <nfeDistDFeInteresse xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe">
                  <nfeDadosMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe">
                    <distDFeInt xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.01">
                      <tpAmb>1</tpAmb>
                      <cUFAutor>{ufAutor}</cUFAutor>
                      <CNPJ>{cnpj}</CNPJ>
                      <consChNFe><chNFe>{accessKey}</chNFe></consChNFe>
                    </distDFeInt>
                  </nfeDadosMsg>
                </nfeDistDFeInteresse>
              </soap:Body>
            </soap:Envelope>
            """;
    }

    public static NfeDistributionResponse ParseResponse(string responseXml, string accessKey)
    {
        if (string.IsNullOrWhiteSpace(responseXml))
            throw new InvalidDataException("Resposta vazia da SEFAZ.");

        var document = ParseSafe(responseXml);
        var ret = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "retDistDFeInt")
            ?? throw new InvalidDataException("Resposta sem retDistDFeInt.");

        var cStat = ChildValue(ret, "cStat") ?? string.Empty;
        var message = ChildValue(ret, "xMotivo") ?? "Resposta recebida da SEFAZ.";
        if (cStat != "138")
            return new NfeDistributionResponse(cStat, message, null);

        foreach (var docZip in ret.Descendants().Where(e => e.Name.LocalName == "docZip"))
        {
            var schema = docZip.Attribute("schema")?.Value ?? string.Empty;
            if (!schema.Contains("procNFe", StringComparison.OrdinalIgnoreCase))
                continue;

            var xml = DecompressDocZip(docZip.Value);
            var xmlDocument = ParseSafe(xml);
            var infNFe = xmlDocument.Descendants().FirstOrDefault(e => e.Name.LocalName == "infNFe");
            var id = infNFe?.Attribute("Id")?.Value;
            if (!string.Equals(id, "NFe" + accessKey, StringComparison.Ordinal))
                continue;

            return new NfeDistributionResponse(cStat, message, xml);
        }

        return new NfeDistributionResponse(cStat, message, null);
    }

    private static XDocument ParseSafe(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxXmlBytes
        };
        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static string? ChildValue(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    private static string DecompressDocZip(string base64)
    {
        byte[] compressed;
        try
        {
            compressed = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("docZip inválido.", ex);
        }

        using var input = new MemoryStream(compressed, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        var total = 0;

        while (true)
        {
            var read = gzip.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            total += read;
            if (total > MaxXmlBytes)
                throw new InvalidDataException("XML distribuído excede o limite permitido.");
            output.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }
}
