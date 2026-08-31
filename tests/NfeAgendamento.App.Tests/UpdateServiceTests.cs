using System.Net;
using System.Net.Http.Json;
using NfeAgendamento.App.Updates;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_detects_newer_release_without_downloading_files()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { tag_name = "v1.2.0" })
        }));
        using var service = new UpdateService(client, new Version(1, 0, 0));

        var result = await service.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(1, 2, 0), result.LatestVersion);
    }

    [Fact]
    public async Task CheckAsync_treats_missing_release_as_no_update()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var service = new UpdateService(client, new Version(1, 0, 0));

        var result = await service.CheckAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.LatestVersion);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
