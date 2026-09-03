using System.Security.Cryptography;
using System.Text;
using NfeAgendamento.App.SharedQueue;
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
    public async Task Shared_cache_written_by_one_candidate_is_readable_by_another_without_plaintext()
    {
        var share = TempDirectory();
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();
        var groupKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var firstState = new CandidateStateStore(Path.Combine(share, "first-state.bin"));
            var secondState = new CandidateStateStore(Path.Combine(share, "second-state.bin"));
            firstState.Save(groupKey);
            secondState.Save(groupKey);
            var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-31T12:00:00Z"));

            var first = new EncryptedXmlCache(paths, firstState, clock, TimeSpan.FromHours(24));
            var second = new EncryptedXmlCache(paths, secondState, clock, TimeSpan.FromHours(24));

            await first.PutAsync(AccessKey, Xml);
            var entry = await second.TryGetAsync(AccessKey);

            Assert.NotNull(entry);
            Assert.Equal(Xml, entry!.Xml);
            var file = Assert.Single(Directory.GetFiles(paths.CacheDirectory, "*.bin"));
            var raw = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
            Assert.DoesNotContain("<nfeProc", raw, StringComparison.Ordinal);
            Assert.DoesNotContain(AccessKey, raw, StringComparison.Ordinal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(groupKey);
        }
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
    public async Task Corrupt_ciphertext_is_deleted_and_treated_as_cache_miss()
    {
        var root = TempDirectory();
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-31T12:00:00Z"));
        var cache = new EncryptedXmlCache(root, clock, TimeSpan.FromHours(24));
        await cache.PutAsync(AccessKey, Xml);
        var file = Assert.Single(Directory.GetFiles(root, "*.bin"));
        await File.WriteAllBytesAsync(file, Encoding.UTF8.GetBytes("not-valid-dpapi-data"));

        var entry = await cache.TryGetAsync(AccessKey);

        Assert.Null(entry);
        Assert.False(File.Exists(file));
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
