using NfeAgendamento.App.Fiscal;

namespace NfeAgendamento.App.SharedQueue;

public sealed record QueueRequestEnvelope(
    int Version,
    Guid RequestId,
    DateTimeOffset CreatedUtc,
    byte[] EncryptedKey,
    byte[] Nonce,
    byte[] Tag,
    byte[] Ciphertext);

public sealed record QueueResponseEnvelope(
    int Version,
    Guid RequestId,
    DateTimeOffset CreatedUtc,
    byte[] Nonce,
    byte[] Tag,
    byte[] Ciphertext);

public sealed record QueueHeartbeat(
    int Version,
    string CentralId,
    DateTimeOffset UpdatedUtc,
    string PublicKeyBase64,
    string AppVersion);

public sealed record QueueLookupPayload(string AccessKey);

public sealed record QueueLookupResponsePayload(
    NfeLookupStatus Status,
    string? Xml,
    string? CStat,
    string? Message,
    bool FromCache,
    DateTimeOffset? BlockedUntilUtc)
{
    public static QueueLookupResponsePayload FromResult(NfeLookupResult result) =>
        new(result.Status, result.Xml, result.CStat, result.Message, result.FromCache, result.BlockedUntilUtc);

    public NfeLookupResult ToResult() =>
        new(Status, Xml, CStat, Message, FromCache, BlockedUntilUtc);
}

public sealed record ClientRequestMaterial(QueueRequestEnvelope Envelope, byte[] AesKey);
public sealed record OpenedQueueRequest(QueueLookupPayload Payload, byte[] AesKey);
