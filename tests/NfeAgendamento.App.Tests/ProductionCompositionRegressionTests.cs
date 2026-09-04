using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class ProductionCompositionRegressionTests
{
    [Fact]
    public void Production_central_key_store_must_use_shared_group_identity()
    {
        var program = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Program.cs"));

        Assert.DoesNotContain("builder.Services.AddSingleton<CentralKeyStore>();", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddSingleton<CentralKeyStore>(sp => new CentralKeyStore(", program, StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<CandidateStateStore>()", program, StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<SharedGroupIdentityStore>()", program, StringComparison.Ordinal);
    }
}
