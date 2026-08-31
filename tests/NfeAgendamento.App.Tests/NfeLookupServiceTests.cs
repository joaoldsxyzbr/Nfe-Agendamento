using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.Storage;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class NfeLookupServiceTests
{
    private const string ValidKey = "35260812345678000195550010000000011000000016";

    [Fact]
    public async Task Lookup_returns_cache_without_calling_transport()
    {
        using var temp = new TemporaryDirectory();
        var cache = new EncryptedXmlCache(temp.Path, TimeProvider.System, TimeSpan.FromHours(24));
        await cache.PutAsync(ValidKey, "<nfeProc>cached</nfeProc>");
        var transport = new FakeTransport(new NfeDistributionResponse("138", "Documento localizado", "<nfeProc>remote</nfeProc>"));
        var service = CreateService(temp.Path, cache, transport);

        var result = await service.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.Found, result.Status);
        Assert.True(result.FromCache);
        Assert.Equal("<nfeProc>cached</nfeProc>", result.Xml);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task Lookup_rejects_invalid_key_before_transport()
    {
        using var temp = new TemporaryDirectory();
        var transport = new FakeTransport(new NfeDistributionResponse("138", "Documento localizado", "<nfeProc />"));
        var service = CreateService(temp.Path, null, transport);

        await Assert.ThrowsAsync<ArgumentException>(() => service.LookupAsync("123"));
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task Lookup_maps_137_to_not_found_without_global_cooldown()
    {
        using var temp = new TemporaryDirectory();
        var transport = new FakeTransport(new NfeDistributionResponse("137", "Nenhum documento localizado", null));
        var cooldown = new FiscalCooldownStore(Path.Combine(temp.Path, "cooldown.bin"));
        var service = CreateService(temp.Path, null, transport, cooldown);

        var result = await service.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.NotFound, result.Status);
        Assert.Null((await cooldown.ReadAsync()).BlockedUntilUtc);
    }

    [Fact]
    public async Task Lookup_on_656_persists_one_hour_cooldown()
    {
        using var temp = new TemporaryDirectory();
        var transport = new FakeTransport(new NfeDistributionResponse("656", "Consumo indevido", null));
        var cooldown = new FiscalCooldownStore(Path.Combine(temp.Path, "cooldown.bin"));
        var before = DateTimeOffset.UtcNow;
        var service = CreateService(temp.Path, null, transport, cooldown);

        var result = await service.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.Blocked, result.Status);
        var blockedUntil = (await cooldown.ReadAsync()).BlockedUntilUtc;
        Assert.NotNull(blockedUntil);
        Assert.InRange(blockedUntil!.Value, before.AddMinutes(59), DateTimeOffset.UtcNow.AddHours(1).AddMinutes(1));
    }

    [Fact]
    public async Task Lookup_found_persists_xml_in_encrypted_cache()
    {
        using var temp = new TemporaryDirectory();
        var cache = new EncryptedXmlCache(Path.Combine(temp.Path, "cache"), TimeProvider.System, TimeSpan.FromHours(24));
        var transport = new FakeTransport(new NfeDistributionResponse("138", "Documento localizado", "<nfeProc>ok</nfeProc>"));
        var service = CreateService(temp.Path, cache, transport);

        var result = await service.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.Found, result.Status);
        Assert.False(result.FromCache);
        Assert.Equal("<nfeProc>ok</nfeProc>", (await cache.TryGetAsync(ValidKey))!.Xml);
    }

    private static NfeLookupService CreateService(
        string root,
        EncryptedXmlCache? cache,
        INfeDistributionTransport transport,
        FiscalCooldownStore? cooldown = null) =>
        new(
            transport,
            cache ?? new EncryptedXmlCache(Path.Combine(root, "cache"), TimeProvider.System, TimeSpan.FromHours(24)),
            cooldown ?? new FiscalCooldownStore(Path.Combine(root, "cooldown.bin")));

    private sealed class FakeTransport(NfeDistributionResponse response) : INfeDistributionTransport
    {
        public int CallCount { get; private set; }

        public Task<NfeDistributionResponse> QueryByAccessKeyAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(response);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-agendamento-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
