using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class ProgramFiscalResultTests
{
    private static string ProgramSource() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Program.cs"));

    [Fact]
    public void Busy_fiscal_result_returns_429_with_retry_after()
    {
        var source = ProgramSource();

        Assert.Contains("NfeLookupStatus.Busy => BusyResult(result)", source, StringComparison.Ordinal);
        Assert.Contains("status = \"fila_ocupada\"", source, StringComparison.Ordinal);
        Assert.Contains("Status429TooManyRequests", source, StringComparison.Ordinal);
        Assert.Contains("RetryAfter = \"5\"", source, StringComparison.Ordinal);
    }
}
