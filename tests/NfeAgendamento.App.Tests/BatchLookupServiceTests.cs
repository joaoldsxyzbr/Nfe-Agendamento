using System.IO.Compression;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.Storage;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class BatchLookupServiceTests
{
    private const string KeyOne = "35260812345678000195550010000000011000000018";
    private const string KeyTwo = "35260812345678000195550010000000011000000026";

    [Fact]
    public async Task LookupAsync_deduplicates_and_processes_keys_sequentially()
    {
        using var temp = new TemporaryDirectory();
        var transport = new FakeTransport(new Dictionary<string, NfeDistributionResponse>
        {
            [KeyOne] = new("138", "Encontrada", "<nfeProc>one</nfeProc>"),
            [KeyTwo] = new("137", "Não encontrada", null)
        });
        var lookup = new NfeLookupService(
            transport,
            new EncryptedXmlCache(Path.Combine(temp.Path, "cache"), TimeProvider.System, TimeSpan.FromHours(24)),
            new FiscalCooldownStore(Path.Combine(temp.Path, "cooldown.bin")));
        var batch = new BatchLookupService(lookup);

        var result = await batch.LookupAsync([KeyOne, KeyOne, KeyTwo]);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal([KeyOne, KeyTwo], result.Items.Select(item => item.AccessKey));
        Assert.Equal(2, transport.CallOrder.Count);
    }

    [Fact]
    public async Task CreateZip_includes_only_found_xmls_and_a_summary()
    {
        var result = new BatchLookupResult([
            new(KeyOne, NfeLookupStatus.Found, "<nfeProc>one</nfeProc>", "Encontrada"),
            new(KeyTwo, NfeLookupStatus.NotFound, null, "Não encontrada")
        ]);

        var zipBytes = BatchLookupService.CreateZip(result);

        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry($"{KeyOne}.xml"));
        Assert.Null(archive.GetEntry($"{KeyTwo}.xml"));
        var summary = archive.GetEntry("resultado.txt");
        Assert.NotNull(summary);
        using var reader = new StreamReader(summary!.Open());
        var text = await reader.ReadToEndAsync();
        Assert.Contains(KeyTwo, text);
    }

    private sealed class FakeTransport(IReadOnlyDictionary<string, NfeDistributionResponse> responses) : INfeDistributionTransport
    {
        public List<string> CallOrder { get; } = [];

        public Task<NfeDistributionResponse> QueryByAccessKeyAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            CallOrder.Add(accessKey);
            return Task.FromResult(responses[accessKey]);
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
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
