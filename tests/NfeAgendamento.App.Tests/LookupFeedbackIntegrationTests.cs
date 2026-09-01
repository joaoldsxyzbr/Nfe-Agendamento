using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class LookupFeedbackIntegrationTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Lookup_feedback_script_loads_before_app_script()
    {
        var html = Fixture("index.html");
        var feedback = html.IndexOf("/lookup-feedback.js", StringComparison.Ordinal);
        var app = html.IndexOf("/app.js", StringComparison.Ordinal);

        Assert.True(feedback >= 0, "lookup-feedback.js precisa ser carregado pela página.");
        Assert.True(app >= 0, "app.js precisa continuar carregado pela página.");
        Assert.True(feedback < app, "lookup-feedback.js deve carregar antes de app.js.");
    }

    [Fact]
    public void Lookup_uses_structured_feedback_and_retry_after_header()
    {
        var script = Fixture("app.js");

        Assert.Contains("NfeLookupFeedback.buildLookupErrorMessage", script, StringComparison.Ordinal);
        Assert.Contains("response.headers.get('Retry-After')", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Consultas temporariamente bloqueadas até", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_client_hides_certificate_admin_when_server_returns_forbidden()
    {
        var script = Fixture("app.js");

        Assert.Contains("listResponse.status === 403", script, StringComparison.Ordinal);
        Assert.Contains("document.querySelector('.certificate-panel')", script, StringComparison.Ordinal);
        Assert.Contains("certificatePanel.hidden = true", script, StringComparison.Ordinal);
    }
}
