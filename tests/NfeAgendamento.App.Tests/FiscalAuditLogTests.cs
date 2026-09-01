using System.Text.Json;
using NfeAgendamento.App.Fiscal;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class FiscalAuditLogTests
{
    private const string AccessKey = "35260812345678000195550010000000011000000018";

    [Fact]
    public async Task Audit_never_persists_full_access_key_and_keeps_operational_fields()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "fiscal-audit.jsonl");
        var log = new FiscalAuditLog(path);
        var result = new NfeLookupResult(NfeLookupStatus.NotFound, null, "137", "Nenhum documento", false);

        await log.RecordAsync(AccessKey, result, TimeSpan.FromMilliseconds(42));

        var text = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain(AccessKey, text, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(text.Trim());
        var root = json.RootElement;
        Assert.Equal(12, root.GetProperty("keyFingerprint").GetString()!.Length);
        Assert.Equal("NotFound", root.GetProperty("status").GetString());
        Assert.Equal("137", root.GetProperty("cStat").GetString());
        Assert.False(root.GetProperty("fromCache").GetBoolean());
        Assert.True(root.GetProperty("durationMs").GetInt64() >= 0);
        Assert.True(root.TryGetProperty("timestampUtc", out _));
    }

    [Fact]
    public async Task Audit_failure_never_breaks_the_caller()
    {
        using var temp = new TemporaryDirectory();
        var log = new FiscalAuditLog(temp.Path);
        var result = new NfeLookupResult(NfeLookupStatus.Failed, null, null, "Falha", false);

        await log.RecordAsync(AccessKey, result, TimeSpan.Zero);
    }

    [Fact]
    public async Task Audit_rotates_when_current_file_reaches_limit()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "fiscal-audit.jsonl");
        var log = new FiscalAuditLog(path, maxFileBytes: 300);
        var result = new NfeLookupResult(NfeLookupStatus.NotFound, null, "137", "Nenhum documento", false);

        for (var i = 0; i < 8; i++)
            await log.RecordAsync(AccessKey, result, TimeSpan.FromMilliseconds(i));

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ".1"));
        Assert.DoesNotContain(AccessKey, await File.ReadAllTextAsync(path), StringComparison.Ordinal);
        Assert.DoesNotContain(AccessKey, await File.ReadAllTextAsync(path + ".1"), StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-agendamento-audit-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
