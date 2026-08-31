using System.Text;
using NfeAgendamento.App.Storage;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class EncryptedXmlCacheTests
{
    private const string AccessKey = "42260812345678000123550010000012341000012342";
    private const string Xml = "<nfeProc><NFe><infNFe Id=\"NFe42260812345678000123550010000012341000012342\" /></NFe></nfeProc>";

    [Fact]
    public async Task Put_then_get_returns_identical_xml_without_plaintext_on_disk()
    {
        var root = TempDirectory();
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-31T12:00:00Z"));
        var cache = new EncryptedXmlCache(root, clock, TimeSpan.FromHours(24));

        await cache.PutAsync(AccessKey, Xml);
        var entry = await cache.TryGetAsync(AccessKey);

        Assert.NotNull(entry);
        Assert.Equal(Xml, entry!.Xml);
        Assert.Equal(AccessKey, entry.AccessKey);

        var file = Assert.Single(Directory.GetFiles(root, "*.bin"));
        var raw = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
        Assert.DoesNotContain("<nfeProc", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessKey, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entry_older_than_24_hours_returns_null_and_is_deleted()
    {
        var root = TempDirectory();
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-31T12:00:00Z"));
        var cache = new EncryptedXmlCache(root, clock, TimeSpan.FromHours(24));
        await cache.PutAsync(AccessKey, Xml);
        var file = Assert.Single(Directory.GetFiles(root, "*.bin"));

        clock.Advance(TimeSpan.FromHours(24).Add(TimeSpan.FromSeconds(1)));
        var entry = await cache.TryGetAsync(AccessKey);

        Assert.Null(entry);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Corrupt_ciphertext_fails_closed()
    {
        var root = TempDirectory();
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-31T12:00:00Z"));
        var cache = new EncryptedXmlCache(root, clock, TimeSpan.FromHours(24));
        await cache.PutAsync(AccessKey, Xml);
        var file = Assert.Single(Directory.GetFiles(root, "*.bin"));
        await File.WriteAllBytesAsync(file, Encoding.UTF8.GetBytes("not-valid-dpapi-data"));

        await Assert.ThrowsAsync<InvalidDataException>(() => cache.TryGetAsync(AccessKey));
    }

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "NfeAgendamento.Tests", Guid.NewGuid().ToString("N"));

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }
}
