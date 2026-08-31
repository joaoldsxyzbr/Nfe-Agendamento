using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NfeAgendamento.App.Certificates;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class CertificateServiceTests
{
    [Fact]
    public void FilterUsable_excludes_expired_future_and_no_private_key_certificates()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        using var valid = CreateCertificate(now.AddDays(-1), now.AddDays(30), keepPrivateKey: true, "CN=Valid");
        using var expired = CreateCertificate(now.AddDays(-30), now.AddDays(-1), keepPrivateKey: true, "CN=Expired");
        using var future = CreateCertificate(now.AddDays(1), now.AddDays(30), keepPrivateKey: true, "CN=Future");
        using var withoutPrivateKey = CreateCertificate(now.AddDays(-1), now.AddDays(30), keepPrivateKey: false, "CN=NoKey");

        var result = CertificateService.FilterUsable(
            new[] { valid, expired, future, withoutPrivateKey },
            now);

        var selected = Assert.Single(result);
        Assert.Equal(valid.Thumbprint, selected.Thumbprint);
        Assert.True(selected.HasPrivateKey);
    }

    [Fact]
    public void ToSelection_exposes_only_safe_metadata()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        using var certificate = CreateCertificate(now.AddDays(-1), now.AddDays(30), keepPrivateKey: true, "CN=Empresa Teste");

        var selection = CertificateService.ToSelection(certificate);

        Assert.Equal(certificate.Thumbprint, selection.Thumbprint);
        Assert.Equal(certificate.Subject, selection.Subject);
        Assert.Equal(certificate.NotAfter, selection.NotAfter);
    }

    [Fact]
    public void Certificate_identity_accepts_explicit_authority_state_when_subject_has_no_state()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        using var certificate = CreateCertificate(now.AddDays(-1), now.AddDays(30), keepPrivateKey: true, "CN=12345678000195, O=Empresa Teste, C=BR");

        var identity = CertificateIdentityReader.Read(certificate, "42");

        Assert.Equal("12345678000195", identity.Cnpj);
        Assert.Equal("42", identity.UfAutor);
    }

    [Fact]
    public void Certificate_identity_prefers_cnpj_suffix_from_common_name_when_subject_contains_other_14_digit_identifier()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        using var certificate = CreateCertificate(
            now.AddDays(-1),
            now.AddDays(30),
            keepPrivateKey: true,
            "CN=PRADO SUPERMERCADO LTDA:09199938000157, OU=37279265000180, O=ICP-Brasil, C=BR");

        var identity = CertificateIdentityReader.Read(certificate, "42");

        Assert.Equal("09199938000157", identity.Cnpj);
        Assert.Equal("42", identity.UfAutor);
    }

    private static X509Certificate2 CreateCertificate(
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        bool keepPrivateKey,
        string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(notBefore, notAfter);

        if (keepPrivateKey)
        {
            return new X509Certificate2(
                generated.Export(X509ContentType.Pfx),
                (string?)null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
        }

        return new X509Certificate2(generated.Export(X509ContentType.Cert));
    }
}
