using NfeAgendamento.App.Portal;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class NfePortalXmlValidatorTests
{
    private const string AccessKey = "35260812345678000195550010000000011000000018";

    [Fact]
    public void Matching_processed_nfe_is_accepted()
    {
        var xml = $"<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\"><NFe><infNFe Id=\"NFe{AccessKey}\" /></NFe></nfeProc>";

        var validated = NfePortalXmlValidator.ValidateAndNormalize(xml, AccessKey);

        Assert.Equal(xml, validated);
    }

    [Fact]
    public void Xml_for_another_access_key_is_rejected()
    {
        var otherKey = "35260812345678000195550010000000021000000015";
        var xml = $"<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\"><NFe><infNFe Id=\"NFe{otherKey}\" /></NFe></nfeProc>";

        var exception = Assert.Throws<InvalidDataException>(() =>
            NfePortalXmlValidator.ValidateAndNormalize(xml, AccessKey));

        Assert.Contains("não corresponde", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dtd_and_external_entity_payload_is_rejected()
    {
        var xml = $"<!DOCTYPE nfeProc [<!ENTITY xxe SYSTEM \"file:///c:/windows/win.ini\">]><nfeProc><NFe><infNFe Id=\"NFe{AccessKey}\">&xxe;</infNFe></NFe></nfeProc>";

        var exception = Assert.Throws<InvalidDataException>(() =>
            NfePortalXmlValidator.ValidateAndNormalize(xml, AccessKey));

        Assert.Contains("segurança", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Non_processed_nfe_root_is_rejected()
    {
        var xml = $"<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\"><infNFe Id=\"NFe{AccessKey}\" /></NFe>";

        var exception = Assert.Throws<InvalidDataException>(() =>
            NfePortalXmlValidator.ValidateAndNormalize(xml, AccessKey));

        Assert.Contains("processado", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
