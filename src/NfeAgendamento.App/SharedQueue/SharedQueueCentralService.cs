using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace NfeAgendamento.App.SharedQueue;

public enum CentralRuntimeStatus
{
    Client,
    Standby,
    Active,
    ShareUnavailable,
    Conflict
}

public sealed class SharedQueueCentralService : BackgroundService
{
    private readonly object _sync = new();
    private readonly CentralStateService _centralState;
    private readonly SharedQueuePaths _paths;
    private readonly CentralKeyStore _keyStore;
    private readonly SharedQueueGroupBootstrapService? _groupBootstrap;
    private readonly SharedQueueGroupRotationService? _rotationService;
    private SharedQueueCentralLease? _lease;
    private CentralRuntimeStatus _status = CentralRuntimeStatus.Client;
    private string? _lastError;
    private DateTimeOffset? _lastHeartbeatUtc;

    public SharedQueueCentralService(
        CentralStateService centralState,
        SharedQueuePaths paths,
        CentralKeyStore keyStore)
        : this(centralState, paths, keyStore, null, null)
    {
    }

    public SharedQueueCentralService(
        CentralStateService centralState,
        SharedQueuePaths paths,
        CentralKeyStore keyStore,
        SharedQueueGroupBootstrapService? groupBootstrap)
        : this(centralState, paths, keyStore, groupBootstrap, null)
    {
    }

    public SharedQueueCentralService(
        CentralStateService centralState,
        SharedQueuePaths paths,
        CentralKeyStore keyStore,
        SharedQueueGroupBootstrapService? groupBootstrap,
        SharedQueueGroupRotationService? rotationService)
    {
        _centralState = centralState ?? throw new ArgumentNullException(nameof(centralState));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _groupBootstrap = groupBootstrap;
        _rotationService = rotationService;
        _centralState.Changed += CentralStateChanged;
    }

    public bool IsActive
    {
        get { lock (_sync) return _status == CentralRuntimeStatus.Active; }
    }

    public CentralRuntimeStatus Status
    {
        get { lock (_sync) return _status; }
    }

    public string? LastError
    {
        get { lock (_sync) return _lastError; }
    }

    public DateTimeOffset? LastHeartbeatUtc
    {
        get { lock (_sync) return _lastHeartbeatUtc; }
    }

    public bool ShareAvailable => _paths.ValidateForClient();

    public bool CanProcessWork()
    {
        SharedQueueCentralLease? lease;
        lock (_sync)
        {
            if (_status != CentralRuntimeStatus.Active || _lease is null)
                return false;
            lease = _lease;
        }

        if (_rotationService?.BlocksFiscalWork == true)
            return false;

        if (lease.IsHealthy)
            return true;

        ReleaseLease(
            CentralRuntimeStatus.ShareUnavailable,
            "O lock exclusivo da fila foi perdido. Nenhum novo trabalho fiscal será iniciado até a liderança ser readquirida.");
        return false;
    }

    public async Task TryActivateOnceAsync(CancellationToken cancellationToken = default)
    {
        var automaticMode = _groupBootstrap is not null;
        if (!automaticMode && !_centralState.IsConfiguredAsCentral)
        {
            ReleaseLease(CentralRuntimeStatus.Client, null);
            return;
        }

        lock (_sync)
        {
            if (_lease is not null)
                return;
        }

        SharedQueueCentralLease? acquired = null;
        var transferred = false;
        try
        {
            if (!Directory.Exists(_paths.Root))
            {
                ReleaseLease(CentralRuntimeStatus.ShareUnavailable, $"A pasta '{SharedQueuePaths.DefaultRoot}' não está disponível.");
                return;
            }

            _paths.InitializeAsCentral();
            cancellationToken.ThrowIfCancellationRequested();

            if (automaticMode)
                _groupBootstrap!.TryImportCandidateBundle();

            acquired = SharedQueueCentralLease.TryAcquire(_paths);
            if (acquired is null)
            {
                ReleaseLease(
                    automaticMode ? CentralRuntimeStatus.Standby : CentralRuntimeStatus.Conflict,
                    automaticMode ? "A fila já está sendo processada por outro PC." : "Já existe outra Central ativa na pasta compartilhada.");
                return;
            }

            if (automaticMode)
            {
                if (_rotationService is not null
                    && !await _rotationService.CompletePendingAsync(cancellationToken))
                {
                    ReleaseLease(
                        CentralRuntimeStatus.Client,
                        "Existe uma rotação de confiança pendente que este PC não conseguiu concluir. Nenhum trabalho fiscal será iniciado.");
                    return;
                }

                await _groupBootstrap!.EnsureBootstrapAsync(cancellationToken);
                if (!_groupBootstrap.IsCandidateReady)
                {
                    ReleaseLease(CentralRuntimeStatus.Client, "Este PC ainda não está autorizado a assumir a fila.");
                    return;
                }
            }

            lock (_sync)
            {
                _lease = acquired;
                transferred = true;
                _status = CentralRuntimeStatus.Active;
                _lastError = null;
            }

            await PublishHeartbeatAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidDataException or InvalidOperationException)
        {
            ReleaseLease(CentralRuntimeStatus.ShareUnavailable, ex.Message);
        }
        finally
        {
            if (!transferred)
                acquired?.Dispose();
        }
    }

    public async Task PublishHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        if (!CanProcessWork())
            return;

        var now = DateTimeOffset.UtcNow;
        var publicKey = _keyStore.GetOrCreatePublicKey();
        var heartbeat = new QueueHeartbeat(
            SharedQueueCrypto.ProtocolVersion,
            Environment.MachineName,
            now,
            Convert.ToBase64String(publicKey),
            typeof(SharedQueueCentralService).Assembly.GetName().Version?.ToString() ?? "0.0.0");

        using (var privateKey = _keyStore.OpenPrivateKey())
            heartbeat = heartbeat with { SignatureBase64 = SharedQueueCrypto.SignHeartbeat(heartbeat, privateKey) };

        var target = _paths.StatusPath("heartbeat.json");
        var temporary = _paths.HeartbeatTemporaryPath(Guid.NewGuid());
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(heartbeat);
            await SharedQueueFileIO.WriteAtomicAsync(
                temporary,
                target,
                bytes,
                SharedQueueFileIO.MaxHeartbeatBytes,
                overwrite: true,
                cancellationToken);

            lock (_sync)
            {
                _lastHeartbeatUtc = now;
                _lastError = null;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ReleaseLease(CentralRuntimeStatus.ShareUnavailable, ex.Message);
        }
        finally
        {
            TryDelete(temporary);
            CryptographicOperations.ZeroMemory(publicKey);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_groupBootstrap is null && !_centralState.IsConfiguredAsCentral)
                {
                    ReleaseLease(CentralRuntimeStatus.Client, null);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                if (!IsActive)
                    await TryActivateOnceAsync(stoppingToken);
                else
                    await PublishHeartbeatAsync(stoppingToken);

                await Task.Delay(IsActive ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        ReleaseLease(CentralRuntimeStatus.Client, null);
    }

    public override void Dispose()
    {
        _centralState.Changed -= CentralStateChanged;
        ReleaseLease(CentralRuntimeStatus.Client, null);
        base.Dispose();
    }

    private void CentralStateChanged(object? sender, EventArgs e)
    {
        if (_groupBootstrap is null && !_centralState.IsConfiguredAsCentral)
            ReleaseLease(CentralRuntimeStatus.Client, null);
    }

    private void ReleaseLease(CentralRuntimeStatus status, string? error)
    {
        SharedQueueCentralLease? lease;
        lock (_sync)
        {
            lease = _lease;
            _lease = null;
            _status = status;
            _lastError = error;
            if (status != CentralRuntimeStatus.Active)
                _lastHeartbeatUtc = null;
        }

        if (lease is not null)
            TryDelete(_paths.StatusPath("heartbeat.json"));
        lease?.Dispose();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
