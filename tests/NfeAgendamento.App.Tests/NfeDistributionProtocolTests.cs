using System.IO.Compression;
using System.Text;
using NfeAgendamento.App.Fiscal;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class NfeDistributionProtocolTests
{
    private const string AccessKey = "35260812345678000195550010000000011000000018";

    [Fact]
    public void BuildSoap_contains_only_consChNFe_query()
    {
        var soap = NfeDistributionProtocol.BuildSoap(AccessKey, "12345678000195", "42");

        Assert.Contains("<tpAmb>1</tpAmb>", soap);
        Assert.Contains("<cUFAutor>42</cUFAutor>", soap);
        Assert.Contains("<CNPJ>12345678000195</CNPJ>", soap);
        Assert.Contains($"<consChNFe><chNFe>{AccessKey}</chNFe></consChNFe>", soap);
        Assert.DoesNotContain("<distNSU>", soap);
    }

    [Fact]
    public void ParseResponse_maps_137_without_document()
    {
        var response = Envelope("""
            <retDistDFeInt xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.01">
              <tpAmb>1</tpAmb><verAplic>1</verAplic><cStat>137</cStat><xMotivo>Nenhum documento localizado</xMotivo>
            </retDistDFeInt>
            """);

        var parsed = NfeDistributionProtocol.ParseResponse(response, AccessKey);

        Assert.Equal("137", parsed.CStat);
        Assert.Null(parsed.Xml);
    }

    [Fact]
    public void ParseResponse_decompresses_matching_procNFe()
    {
        var xml = $"<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\"><NFe><infNFe Id=\"NFe{AccessKey}\" /></NFe></nfeProc>";
        var docZip = GzipBase64(xml);
        var response = Envelope($$"""
            <retDistDFeInt xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.01">
              <tpAmb>1</tpAmb><verAplic>1</verAplic><cStat>138</cStat><xMotivo>Documento localizado</xMotivo>
              <loteDistDFeInt><docZip NSU="000000000000001" schema="procNFe_v4.00.xsd">{{docZip}}</docZip></loteDistDFeInt>
            </retDistDFeInt>
            """);

        var parsed = NfeDistributionProtocol.ParseResponse(response, AccessKey);

        Assert.Equal("138", parsed.CStat);
        Assert.Equal(xml, parsed.Xml);
    }

    [Fact]
    public void ParseResponse_ignores_document_for_another_key()
    {
        const string otherKey = "35260812345678000195550010000000011000000026";
        var xml = $"<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\"><NFe><infNFe Id=\"NFe{otherKey}\" /></NFe></nfeProc>";
        var response = Envelope($$"""
            <retDistDFeInt xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.01">
              <cStat>138</cStat><xMotivo>Documento localizado</xMotivo>
              <loteDistDFeInt><docZip NSU="1" schema="procNFe_v4.00.xsd">{{GzipBase64(xml)}}</docZip></loteDistDFeInt>
            </retDistDFeInt>
            """);

        var parsed = NfeDistributionProtocol.ParseResponse(response, AccessKey);

        Assert.Equal("138", parsed.CStat);
        Assert.Null(parsed.Xml);
    }

    private static string Envelope(string body) => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>{body}</soap:Body></soap:Envelope>
        """;

    private static string GzipBase64(string value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
            writer.Write(value);
        return Convert.ToBase64String(output.ToArray());
    }
}
