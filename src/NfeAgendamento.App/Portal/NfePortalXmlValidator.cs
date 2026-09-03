using System.Text;
using System.Xml;
using System.Xml.Linq;
using NfeAgendamento.App.Fiscal;

namespace NfeAgendamento.App.Portal;

public static class NfePortalXmlValidator
{
    private const int MaxXmlBytes = 10 * 1024 * 1024;

    public static string ValidateAndNormalize(string xml, string accessKey)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidDataException("O Portal da NF-e retornou um XML vazio.");
        if (Encoding.UTF8.GetByteCount(xml) > MaxXmlBytes)
            throw new InvalidDataException("O XML baixado pelo Portal excede o limite permitido.");

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxXmlBytes
            };
            using var stringReader = new StringReader(xml);
            using var reader = XmlReader.Create(stringReader, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("O XML baixado pelo Portal da NF-e não pôde ser validado com segurança.", ex);
        }

        if (!string.Equals(document.Root?.Name.LocalName, "nfeProc", StringComparison.Ordinal))
            throw new InvalidDataException("O arquivo baixado não contém um XML processado de NF-e reconhecido.");

        var infNFe = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "infNFe")
            ?? throw new InvalidDataException("O XML baixado não contém a identificação da NF-e.");
        var id = infNFe.Attribute("Id")?.Value;

        if (!string.Equals(id, "NFe" + accessKey, StringComparison.Ordinal))
            throw new InvalidDataException("O XML baixado não corresponde à chave NF-e consultada.");

        return xml;
    }
}