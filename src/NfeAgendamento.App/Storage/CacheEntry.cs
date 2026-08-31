namespace NfeAgendamento.App.Storage;

public sealed record CacheEntry(
    string AccessKey,
    DateTimeOffset StoredAtUtc,
    string Xml);
