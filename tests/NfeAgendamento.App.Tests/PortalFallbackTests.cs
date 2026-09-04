using System.Reflection;
using System.Runtime.InteropServices;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.Portal;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class PortalFallbackTests
{
    private const string AccessKey = "35260812345678000195550010000000011000000018";
    private const string OtherKey = "35260812345678000195550010000000021000000015";

    [Fact]
    public void Portal_xml_validator_accepts_procNFe_for_requested_key()
    {
        var xml = XmlFor(AccessKey);

        var normalized = InvokeValidator(xml, AccessKey);

        Assert.Equal(xml, normalized);
    }

    [Fact]
    public void Portal_xml_validator_rejects_xml_from_another_access_key()
    {
        var exception = InvokeValidatorExpectingFailure(XmlFor(OtherKey), AccessKey);

        Assert.IsType<InvalidDataException>(exception);
        Assert.Contains("chave", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Portal_xml_validator_rejects_dtd()
    {
        var xml = $"<!DOCTYPE nfeProc [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><nfeProc><NFe><infNFe Id=\"NFe{AccessKey}\">&xxe;</infNFe></NFe></nfeProc>";

        var exception = InvokeValidatorExpectingFailure(xml, AccessKey);

        Assert.True(exception is InvalidDataException or System.Xml.XmlException);
    }

    [Fact]
    public void Portal_form_classifies_only_expected_browser_lifecycle_failures()
    {
        Assert.True(InvokeLifecycleClassifier(new ObjectDisposedException("webview")));
        Assert.True(InvokeLifecycleClassifier(new InvalidOperationException("controle encerrado")));
        Assert.True(InvokeLifecycleClassifier(new COMException("RPC desconectado", unchecked((int)0x80010108))));
        Assert.True(InvokeLifecycleClassifier(new COMException("objeto fechado", unchecked((int)0x80000013))));
        Assert.True(InvokeLifecycleClassifier(new COMException("operação abortada", unchecked((int)0x80004004))));

        Assert.False(InvokeLifecycleClassifier(new COMException("falha COM genérica", unchecked((int)0x80004005))));
        Assert.False(InvokeLifecycleClassifier(new IOException("falha de disco")));
    }

    private static string XmlFor(string accessKey) =>
        $"<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\"><NFe><infNFe Id=\"NFe{accessKey}\"><ide><cUF>35</cUF></ide></infNFe></NFe></nfeProc>";

    private static Type ValidatorType()
    {
        var type = typeof(NfeLookupService).Assembly.GetType("NfeAgendamento.App.Portal.NfePortalXmlValidator");
        Assert.NotNull(type);
        return type!;
    }

    private static MethodInfo ValidatorMethod()
    {
        var method = ValidatorType().GetMethod(
            "ValidateAndNormalize",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(string), typeof(string)],
            modifiers: null);
        Assert.NotNull(method);
        return method!;
    }

    private static string InvokeValidator(string xml, string accessKey) =>
        Assert.IsType<string>(ValidatorMethod().Invoke(null, [xml, accessKey]));

    private static Exception InvokeValidatorExpectingFailure(string xml, string accessKey)
    {
        var exception = Assert.Throws<TargetInvocationException>(() => ValidatorMethod().Invoke(null, [xml, accessKey]));
        Assert.NotNull(exception.InnerException);
        return exception.InnerException!;
    }

    private static bool InvokeLifecycleClassifier(Exception exception)
    {
        var method = typeof(PortalNfeFallbackForm).GetMethod(
            "IsBrowserLifecycleException",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(Exception)],
            modifiers: null);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(null, [exception]));
    }
}
