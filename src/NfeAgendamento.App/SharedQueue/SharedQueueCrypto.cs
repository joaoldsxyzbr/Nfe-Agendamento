using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NfeAgendamento.App.Fiscal;

namespace NfeAgendamento.App.SharedQueue;

public static class SharedQueueCrypto
{
    public const int ProtocolVersion = 1;
    private const int AesKeySize = 32;
    private const int ClientSecretSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int ClientAuthTagSize = 32;

    public static ClientRequestMaterial CreateClientRequest(
        Guid requestId,
        string accessKey,
        byte[] centralPublicKey)
    {
        return CreateClientRequestCore(requestId, accessKey, centralPublicKey, Guid.Empty, 0, null);
    }

    public static ClientRequestMaterial CreateClientRequest(
        Guid requestId,
        string accessKey,
        byte[] centralPublicKey,
        Guid clientId,
        long sequence,
        byte[] clientSecret)
    {
        if (clientId == Guid.Empty)
            throw new ArgumentException("Identidade do cliente inválida.", nameof(clientId));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        ValidateClientSecret(clientSecret);

        return CreateClientRequestCore(requestId, accessKey, centralPublicKey, clientId, sequence, clientSecret);
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
            try
            {
                using (var aes = new AesGcm(aesKey, TagSize))
                    aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, AssociatedData(envelope.RequestId, "request"));

                var payload = JsonSerializer.Deserialize<QueueLookupPayload>(plaintext)
                    ?? throw new CryptographicException("Payload da requisição inválido.");
                if (!AccessKeyValidator.IsValid(payload.AccessKey))
                    throw new CryptographicException("Payload contém chave NF-e inválida.");

                return new OpenedQueueRequest(payload, aesKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
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

        try
        {
            using (var aes = new AesGcm(aesKey, TagSize))
                aes.Encrypt(nonce, payload, ciphertext, tag, AssociatedData(requestId, "response"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

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
        try
        {
            using (var aes = new AesGcm(aesKey, TagSize))
                aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, AssociatedData(envelope.RequestId, "response"));

            var payload = JsonSerializer.Deserialize<QueueLookupResponsePayload>(plaintext)
                ?? throw new CryptographicException("Payload da resposta inválido.");
            return payload.ToResult();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static bool VerifyClientAuthentication(QueueRequestEnvelope envelope, byte[] clientSecret)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateClientSecret(clientSecret);
        if (envelope.ClientId == Guid.Empty
            || envelope.Sequence <= 0
            || envelope.ClientAuthTag is null
            || envelope.ClientAuthTag.Length != ClientAuthTagSize)
        {
            return false;
        }

        var expected = ComputeClientAuthentication(envelope, clientSecret);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, envelope.ClientAuthTag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    public static string SignHeartbeat(QueueHeartbeat heartbeat, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        ArgumentNullException.ThrowIfNull(privateKey);
        var signature = privateKey.SignData(
            HeartbeatAuthenticationData(heartbeat),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        try
        {
            return Convert.ToBase64String(signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    public static bool VerifyHeartbeatSignature(QueueHeartbeat heartbeat, byte[] pinnedPublicKey)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        ArgumentNullException.ThrowIfNull(pinnedPublicKey);
        if (string.IsNullOrWhiteSpace(heartbeat.SignatureBase64))
            return false;

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(heartbeat.SignatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(pinnedPublicKey, out _);
            return rsa.VerifyData(
                HeartbeatAuthenticationData(heartbeat),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private static ClientRequestMaterial CreateClientRequestCore(
        Guid requestId,
        string accessKey,
        byte[] centralPublicKey,
        Guid clientId,
        long sequence,
        byte[]? clientSecret)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));
        ArgumentNullException.ThrowIfNull(centralPublicKey);

        var aesKey = RandomNumberGenerator.GetBytes(AesKeySize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var payload = JsonSerializer.SerializeToUtf8Bytes(new QueueLookupPayload(accessKey));
        var ciphertext = new byte[payload.Length];

        try
        {
            using (var aes = new AesGcm(aesKey, TagSize))
                aes.Encrypt(nonce, payload, ciphertext, tag, AssociatedData(requestId, "request"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(centralPublicKey, out _);
        var encryptedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);
        var envelope = new QueueRequestEnvelope(
            ProtocolVersion,
            requestId,
            DateTimeOffset.UtcNow,
            encryptedKey,
            nonce,
            tag,
            ciphertext,
            clientId,
            sequence,
            null);

        if (clientSecret is not null)
            envelope = envelope with { ClientAuthTag = ComputeClientAuthentication(envelope, clientSecret) };

        return new ClientRequestMaterial(envelope, aesKey);
    }

    private static byte[] ComputeClientAuthentication(QueueRequestEnvelope envelope, byte[] clientSecret)
    {
        var data = RequestAuthenticationData(envelope);
        try
        {
            return HMACSHA256.HashData(clientSecret, data);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    private static byte[] RequestAuthenticationData(QueueRequestEnvelope envelope)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(envelope.Version);
        writer.Write(envelope.RequestId.ToByteArray());
        writer.Write(envelope.CreatedUtc.UtcTicks);
        writer.Write(envelope.ClientId.ToByteArray());
        writer.Write(envelope.Sequence);
        WriteBytes(writer, envelope.EncryptedKey);
        WriteBytes(writer, envelope.Nonce);
        WriteBytes(writer, envelope.Tag);
        WriteBytes(writer, envelope.Ciphertext);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] HeartbeatAuthenticationData(QueueHeartbeat heartbeat)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(heartbeat.Version);
        writer.Write(heartbeat.CentralId ?? string.Empty);
        writer.Write(heartbeat.UpdatedUtc.UtcTicks);
        writer.Write(heartbeat.PublicKeyBase64 ?? string.Empty);
        writer.Write(heartbeat.AppVersion ?? string.Empty);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteBytes(BinaryWriter writer, byte[]? value)
    {
        if (value is null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(value.Length);
        writer.Write(value);
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

    private static void ValidateClientSecret(byte[] clientSecret)
    {
        ArgumentNullException.ThrowIfNull(clientSecret);
        if (clientSecret.Length != ClientSecretSize)
            throw new CryptographicException("Segredo de autenticação do cliente inválido.");
    }
}
