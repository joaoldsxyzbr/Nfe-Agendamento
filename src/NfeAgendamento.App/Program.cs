using NfeAgendamento.App.Security;

namespace NfeAgendamento.App;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var builder = WebApplication.CreateBuilder(args);
        LocalHost.Configure(builder);
        builder.Services.AddSingleton<CsrfTokenService>();

        var app = builder.Build();
        app.UseMiddleware<LocalRequestSecurityMiddleware>();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/bootstrap", (CsrfTokenService csrf) =>
            Results.Ok(new { csrfToken = csrf.CurrentToken }));

        await app.RunAsync();
    }
}
