using System.Security.Cryptography;
using System.Text.Json;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueCryptoTests
{
    private const string AccessKey = "42260912345678000123550010000000011000000015";

    [Fact]
    public void Request_envelope_does_not_expose_access_key_and_round_trips()
    {
        using var rsa = RSA.Create(2048);
        var created = SharedQueueCrypto.CreateClientRequest(
            Guid.NewGuid(),
            AccessKey,
            rsa.ExportSubjectPublicKeyInfo());

        var json = JsonSerializer.Serialize(created.Envelope);
        Assert.DoesNotContain(AccessKey, json, StringComparison.Ordinal);
        Assert.Equal(32, created.AesKey.Length);

        var opened = SharedQueueCrypto.OpenRequest(created.Envelope, rsa);
        Assert.Equal(AccessKey, opened.Payload.AccessKey);
        Assert.Equal(created.AesKey, opened.AesKey);
    }

    [Fact]
    public void Tampered_request_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var created = SharedQueueCrypto.CreateClientRequest(Guid.NewGuid(), AccessKey, rsa.ExportSubjectPublicKeyInfo());
        created.Envelope.Ciphertext[0] ^= 0x01;

        Assert.Throws<CryptographicException>(() => SharedQueueCrypto.OpenRequest(created.Envelope, rsa));
    }

    [Fact]
    public void Response_does_not_expose_xml_and_round_trips()
    {
        var requestId = Guid.NewGuid();
        var aesKey = RandomNumberGenerator.GetBytes(32);
        var expected = new NfeLookupResult(
            NfeLookupStatus.Found,
            "<nfeProc>xml-secreto</nfeProc>",
            "138",
            "Documento localizado.",
            false);

        var envelope = SharedQueueCrypto.CreateResponse(requestId, expected, aesKey);
        var json = JsonSerializer.Serialize(envelope);

        Assert.DoesNotContain("xml-secreto", json, StringComparison.Ordinal);
        Assert.Equal(expected, SharedQueueCrypto.OpenResponse(envelope, aesKey));
    }

    [Fact]
    public void Wrong_response_key_is_rejected()
    {
        var requestId = Guid.NewGuid();
        var aesKey = RandomNumberGenerator.GetBytes(32);
        var wrongKey = RandomNumberGenerator.GetBytes(32);
        var result = new NfeLookupResult(NfeLookupStatus.NotFound, null, "137", "Não localizado.", false);
        var envelope = SharedQueueCrypto.CreateResponse(requestId, result, aesKey);

        Assert.Throws<CryptographicException>(() => SharedQueueCrypto.OpenResponse(envelope, wrongKey));
    }

    [Fact]
    public async Task Pending_secret_store_is_local_protected_and_round_trips()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-agendamento-pending-tests", Guid.NewGuid().ToString("N"));
        var store = new PendingRequestSecretStore(root);
        var id = Guid.NewGuid();
        var secret = RandomNumberGenerator.GetBytes(32);
        try
        {
            await store.SaveAsync(id, secret);
            var path = store.PathForTesting(id);
            var raw = await File.ReadAllBytesAsync(path);

            Assert.NotEqual(secret, raw);
            Assert.Equal(secret, await store.LoadAsync(id));

            store.Delete(id);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
