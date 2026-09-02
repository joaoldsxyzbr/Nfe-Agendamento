using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NfeAgendamento.App.Fiscal;

namespace NfeAgendamento.App.SharedQueue;

public static class SharedQueueCrypto
{
    public const int ProtocolVersion = 1;
    private const int AesKeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static ClientRequestMaterial CreateClientRequest(
        Guid requestId,
        string accessKey,
        byte[] centralPublicKey)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));
        ArgumentNullException.ThrowIfNull(centralPublicKey);

        var aesKey = RandomNumberGenerator.GetBytes(AesKeySize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var payload = JsonSerializer.SerializeToUtf8Bytes(new QueueLookupPayload(accessKey));
        var ciphertext = new byte[payload.Length];

        using (var aes = new AesGcm(aesKey, TagSize))
            aes.Encrypt(nonce, payload, ciphertext, tag, AssociatedData(requestId, "request"));

        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(centralPublicKey, out _);
        var encryptedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);

        return new ClientRequestMaterial(
            new QueueRequestEnvelope(
                ProtocolVersion,
                requestId,
                DateTimeOffset.UtcNow,
                encryptedKey,
                nonce,
                tag,
                ciphertext),
            aesKey);
    }

    public static OpenedQueueRequest OpenRequest(QueueRequestEnvelope envelope, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(privateKey);
        ValidateEnvelope(envelope.Version, envelope.RequestId, envelope.Nonce, envelope.Tag, envelope.Ciphertext);
        if (envelope.EncryptedKey is null || envelope.EncryptedKey.Length == 0)
            throw new CryptographicException("Envelope sem chave protegida.");

        var aesKey = privateKey.Decrypt(envelope.EncryptedKey, RSAEncryptionPadding.OaepSHA256);
        if (aesKey.Length != AesKeySize)
        {
            CryptographicOperations.ZeroMemory(aesKey);
            throw new CryptographicException("Chave de sessão inválida.");
        }

        try
        {
            var plaintext = new byte[envelope.Ciphertext.Length];
            using (var aes = new AesGcm(aesKey, TagSize))
                aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, AssociatedData(envelope.RequestId, "request"));

            var payload = JsonSerializer.Deserialize<QueueLookupPayload>(plaintext)
                ?? throw new CryptographicException("Payload da requisição inválido.");
            if (!AccessKeyValidator.IsValid(payload.AccessKey))
                throw new CryptographicException("Payload contém chave NF-e inválida.");

            return new OpenedQueueRequest(payload, aesKey);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(aesKey);
            throw;
        }
    }

    public static QueueResponseEnvelope CreateResponse(Guid requestId, NfeLookupResult result, byte[] aesKey)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateAesKey(aesKey);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var payload = JsonSerializer.SerializeToUtf8Bytes(QueueLookupResponsePayload.FromResult(result));
        var ciphertext = new byte[payload.Length];

        using (var aes = new AesGcm(aesKey, TagSize))
            aes.Encrypt(nonce, payload, ciphertext, tag, AssociatedData(requestId, "response"));

        return new QueueResponseEnvelope(
            ProtocolVersion,
            requestId,
            DateTimeOffset.UtcNow,
            nonce,
            tag,
            ciphertext);
    }

    public static NfeLookupResult OpenResponse(QueueResponseEnvelope envelope, byte[] aesKey)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateAesKey(aesKey);
        ValidateEnvelope(envelope.Version, envelope.RequestId, envelope.Nonce, envelope.Tag, envelope.Ciphertext);

        var plaintext = new byte[envelope.Ciphertext.Length];
        using (var aes = new AesGcm(aesKey, TagSize))
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, AssociatedData(envelope.RequestId, "response"));

        var payload = JsonSerializer.Deserialize<QueueLookupResponsePayload>(plaintext)
            ?? throw new CryptographicException("Payload da resposta inválido.");
        return payload.ToResult();
    }

    private static byte[] AssociatedData(Guid requestId, string direction) =>
        Encoding.UTF8.GetBytes($"nfe-agendamento:v{ProtocolVersion}:{requestId:N}:{direction}");

    private static void ValidateEnvelope(int version, Guid requestId, byte[] nonce, byte[] tag, byte[] ciphertext)
    {
        if (version != ProtocolVersion || requestId == Guid.Empty)
            throw new CryptographicException("Versão ou identificador de envelope inválido.");
        if (nonce is null || nonce.Length != NonceSize)
            throw new CryptographicException("Nonce inválido.");
        if (tag is null || tag.Length != TagSize)
            throw new CryptographicException("Tag de autenticação inválida.");
        if (ciphertext is null || ciphertext.Length == 0)
            throw new CryptographicException("Conteúdo cifrado inválido.");
    }

    private static void ValidateAesKey(byte[] aesKey)
    {
        ArgumentNullException.ThrowIfNull(aesKey);
        if (aesKey.Length != AesKeySize)
            throw new CryptographicException("Chave AES inválida.");
    }
}
