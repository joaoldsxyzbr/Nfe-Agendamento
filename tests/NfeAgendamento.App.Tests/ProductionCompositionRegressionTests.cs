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

    [Fact]
    public void Production_pairing_uses_atomic_coordinator()
    {
        var program = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Program.cs"));

        Assert.Contains("builder.Services.AddSingleton<SharedQueuePairingCoordinator>();", program, StringComparison.Ordinal);
        Assert.Contains("SharedQueuePairingCoordinator pairing", program, StringComparison.Ordinal);
        Assert.Contains("await pairing.PairAsync(request.Code, cancellationToken)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("group.TryImportCandidateBundle()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_registers_recoverable_group_rotation_before_central_service()
    {
        var program = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Program.cs"));

        var storage = program.IndexOf("builder.Services.AddSingleton<SharedQueueGroupRotationStorage>();", StringComparison.Ordinal);
        var rotation = program.IndexOf("builder.Services.AddSingleton<SharedQueueGroupRotationService>();", StringComparison.Ordinal);
        var central = program.IndexOf("builder.Services.AddSingleton<SharedQueueCentralService>();", StringComparison.Ordinal);

        Assert.True(storage >= 0, "Produção deve registrar o armazenamento de rotação recuperável.");
        Assert.True(rotation > storage, "O serviço de rotação deve ser registrado depois do armazenamento.");
        Assert.True(central > rotation, "A Central deve ser composta depois do serviço de rotação para receber a dependência.");
    }

    [Fact]
    public void Authorized_client_management_is_leader_only_and_never_serializes_client_secrets()
    {
        var program = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Program.cs"));

        var listStart = program.IndexOf("app.MapGet(\"/api/pairing/clients\"", StringComparison.Ordinal);
        var revokeStart = program.IndexOf("app.MapPost(\"/api/pairing/revoke\"", StringComparison.Ordinal);
        var certificatesStart = program.IndexOf("app.MapGet(\"/api/certificates\"", StringComparison.Ordinal);

        Assert.True(listStart >= 0, "A API deve listar os PCs autorizados.");
        Assert.True(revokeStart > listStart, "A API deve permitir revogação pelo líder.");
        Assert.True(certificatesStart > revokeStart, "Os endpoints de dispositivos devem ficar no bloco de pareamento.");

        var managementBlock = program[listStart..certificatesStart];
        Assert.Contains("if (!central.IsActive)", managementBlock, StringComparison.Ordinal);
        Assert.Contains("SharedQueueGroupRotationService rotation", managementBlock, StringComparison.Ordinal);
        Assert.Contains("await rotation.RevokeAsync(request.ClientId, cancellationToken)", managementBlock, StringComparison.Ordinal);
        Assert.DoesNotContain(".Secret", managementBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_pairing_blocks_duplicate_submissions()
    {
        var pairing = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pairing.js"));

        Assert.Contains("let pairingInFlight = false", pairing, StringComparison.Ordinal);
        Assert.Contains("if (pairingInFlight) return", pairing, StringComparison.Ordinal);
        Assert.Contains("pairingInFlight = true", pairing, StringComparison.Ordinal);
        Assert.Contains("pairingInFlight = false", pairing, StringComparison.Ordinal);
    }
}
