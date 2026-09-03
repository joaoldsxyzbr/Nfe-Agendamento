using Microsoft.Extensions.DependencyInjection;
using NfeAgendamento.App.SharedQueue;

namespace NfeAgendamento.App.Fiscal;

public sealed class LookupDispatchService
{
    private readonly IServiceProvider _services;
    private readonly SharedQueueClient _sharedQueueClient;

    public LookupDispatchService(
        IServiceProvider services,
        CentralStateService centralState,
        SharedQueueClient sharedQueueClient)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _ = centralState ?? throw new ArgumentNullException(nameof(centralState));
        _sharedQueueClient = sharedQueueClient ?? throw new ArgumentNullException(nameof(sharedQueueClient));
    }

    public Task<NfeLookupResult> LookupAsync(string accessKey, CancellationToken cancellationToken = default)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));

        var runtime = _services.GetRequiredService<SharedQueueCentralService>();
        if (runtime.IsActive)
        {
            return _services
                .GetRequiredService<NfeLookupService>()
                .LookupAsync(accessKey, cancellationToken);
        }

        return _sharedQueueClient.LookupAsync(accessKey, cancellationToken);
    }
}
