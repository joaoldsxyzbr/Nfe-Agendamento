using Microsoft.AspNetCore.Hosting;

namespace NfeAgendamento.App;

public static class LocalHost
{
    public const string ListenUrl = "http://127.0.0.1:17345";
    public const string LanListenUrl = "http://0.0.0.0:17345";
    public const string LanBrowserUrl = "http://nfeagendamento.local:17345";
    public const int MaxRequestBodyBytes = 256 * 1024;

    public static bool IsLanMode { get; private set; }

    public static string GetListenUrl(string[]? args)
    {
        return IsLanEnabled(args) ? LanListenUrl : ListenUrl;
    }

    public static string GetBrowserUrl(string[]? args)
    {
        if (!IsLanEnabled(args))
            return ListenUrl;
        return LanBrowserUrl;
    }

    public static void Configure(WebApplicationBuilder builder, string[]? args = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IsLanMode = IsLanEnabled(args);
        builder.WebHost.UseUrls(IsLanMode ? LanListenUrl : ListenUrl);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        });
    }

    private static bool IsLanEnabled(string[]? args) =>
        args?.Any(argument => string.Equals(argument, "--lan", StringComparison.OrdinalIgnoreCase)) == true;
}
