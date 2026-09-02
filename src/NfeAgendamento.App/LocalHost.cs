using Microsoft.AspNetCore.Hosting;

namespace NfeAgendamento.App;

public static class LocalHost
{
    public const int Port = 17345;
    public const string ListenUrl = "http://127.0.0.1:17345";
    public const int MaxRequestBodyBytes = 256 * 1024;

    public static string GetListenUrl(string[]? args = null) => ListenUrl;
    public static string GetBrowserUrl(string[]? args = null) => ListenUrl;

    public static void Configure(WebApplicationBuilder builder, string[]? args = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WebHost.UseUrls(ListenUrl);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        });
    }
}
