using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NfeAgendamento.App.SharedQueue;

public sealed record PairingCodeInfo(string Code, DateTimeOffset ExpiresUtc);
public sealed record PairingResult(bool Success, string Message);

public sealed record QueuePairingRequestEnvelope(
    int Version,
    Guid RequestId,
    DateTimeOffset CreatedUtc,
    byte[] Nonce,
    byte[] Tag,
    byte[] Ciphertext);

public sealed record QueuePairingResponseEnvelope(
    int Version,
    Guid RequestId,
    DateTimeOffset CreatedUtc,
    byte[] Nonce,
    byte[] Tag,
    byte[] Ciphertext);

public sealed record QueuePairingRequestPayload(Guid ClientId, string ClientName);

public sealed record QueuePairingResponsePayload(
    Guid ClientId,
    string ClientName,
    byte[] ClientSecret,
    string CentralId,
    byte[] CentralPublicKey);

public sealed record ClientPairingState(
    Guid ClientId,
    string ClientName,
    byte[] ClientSecret,
    byte[] CentralPublicKey,
    string CentralId,
    long NextSequence);

public sealed record ClientRequestCredentials(
    Guid ClientId,
    string ClientName,
    byte[] ClientSecret,
    byte[] CentralPublicKey,
    string CentralId,
    long Sequence);

public sealed class PairingCodeService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly object _sync = new();
    private string? _normalizedCode;
    private DateTimeOffset _expiresUtc;

    public PairingCodeInfo Generate()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        var formatted = string.Join('-', Enumerable.Range(0, 6).Select(index => raw.Substring(index * 4, 4)));
        var expires = DateTimeOffset.UtcNow.Add(Lifetime);
        lock (_sync)
        {
            _normalizedCode = raw;
            _expiresUtc = expires;
        }
        return new PairingCodeInfo(formatted, expires);
    }

    public bool TryGetActiveKey(out byte[] key)
    {
        string? code;
        lock (_sync)
        {
            if (_normalizedCode is null || DateTimeOffset.UtcNow >= _expiresUtc)
            {
                _normalizedCode = null;
                key = Array.Empty<byte>();
                return false;
            }
            code = _normalizedCode;
        }

        key = DeriveKey(code);
        return true;
    }

    public static byte[] DeriveKey(string code)
    {
        var normalized = Normalize(code);
        return SHA256.HashData(Encoding.ASCII.GetBytes($"nfe-agendamento-pairing-v1:{normalized}"));
    }

    private static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Informe o código de pareamento.", nameof(code));

        var normalized = new string(code.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (normalized.Length != 24 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Código de pareamento inválido.", nameof(code));
        return normalized;
    }
}

public sealed class ClientPairingStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NfeAgendamento.SharedQueue.ClientPairing.v1");
    private readonly object _sync = new();
    private readonly string _path;

    public ClientPairingStore()
        : this(Path.Combine(AppPaths.StateRoot, "shared-queue", "client-pairing.bin"))
    {
    }

    public ClientPairingStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Caminho local do pareamento inválido.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public bool IsPaired => Load() is not null;

    public ClientPairingState? Load()
    {
        lock (_sync)
        {
            return Clone(LoadUnlocked());
        }
    }

    public void SavePaired(
        Guid clientId,
        string clientName,
        byte[] clientSecret,
        byte[] centralPublicKey,
        string centralId)
    {
        if (clientId == Guid.Empty)
            throw new ArgumentException("Identidade do cliente inválida.", nameof(clientId));
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentException("Nome do cliente inválido.", nameof(clientName));
        if (clientSecret is null || clientSecret.Length != 32)
            throw new CryptographicException("Segredo do cliente inválido.");
        if (centralPublicKey is null || centralPublicKey.Length == 0)
            throw new CryptographicException("Chave pública da Central inválida.");
        if (string.IsNullOrWhiteSpace(centralId))
            throw new ArgumentException("Identidade da Central inválida.", nameof(centralId));

        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(centralPublicKey, out _);

        lock (_sync)
        {
            SaveUnlocked(new ClientPairingState(
                clientId,
                clientName.Trim(),
                clientSecret.ToArray(),
                centralPublicKey.ToArray(),
                centralId.Trim(),
                0));
        }
    }

    public ClientRequestCredentials ReserveCredentials()
    {
        lock (_sync)
        {
            var state = LoadUnlocked()
                ?? throw new InvalidOperationException("Este PC ainda não foi pareado com a Central.");
            var sequence = checked(state.NextSequence + 1);
            var updated = state with { NextSequence = sequence };
            SaveUnlocked(updated);
            return new ClientRequestCredentials(
                updated.ClientId,
                updated.ClientName,
                updated.ClientSecret.ToArray(),
                updated.CentralPublicKey.ToArray(),
                updated.CentralId,
                sequence);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private ClientPairingState? LoadUnlocked()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            try
            {
                var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    var state = JsonSerializer.Deserialize<ClientPairingState>(plainBytes);
                    if (state is null
                        || state.ClientId == Guid.Empty
                        || string.IsNullOrWhiteSpace(state.ClientName)
                        || state.ClientSecret is null
                        || state.ClientSecret.Length != 32
                        || state.CentralPublicKey is null
                        || state.CentralPublicKey.Length == 0
                        || string.IsNullOrWhiteSpace(state.CentralId)
                        || state.NextSequence < 0)
                    {
                        return null;
                    }
                    return state;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    private void SaveUnlocked(ClientPairingState state)
    {
        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(state);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Diretório local do pareamento inválido.");
            Directory.CreateDirectory(directory);
            var temporary = _path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(protectedBytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, _path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static ClientPairingState? Clone(ClientPairingState? state) =>
        state is null
            ? null
            : state with
            {
                ClientSecret = state.ClientSecret.ToArray(),
                CentralPublicKey = state.CentralPublicKey.ToArray()
            };
}

public sealed class AuthorizedClientStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NfeAgendamento.SharedQueue.AuthorizedClients.v1");
    private readonly object _sync = new();
    private readonly string _path;

    public AuthorizedClientStore()
        : this(Path.Combine(AppPaths.StateRoot, "shared-queue", "authorized-clients.bin"))
    {
    }

    public AuthorizedClientStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Caminho da lista de clientes inválido.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public int Count
    {
        get { lock (_sync) return LoadUnlocked().Count; }
    }

    public void Authorize(Guid clientId, string clientName, byte[] secret)
    {
        if (clientId == Guid.Empty)
            throw new ArgumentException("Identidade do cliente inválida.", nameof(clientId));
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentException("Nome do cliente inválido.", nameof(clientName));
        if (secret is null || secret.Length != 32)
            throw new CryptographicException("Segredo do cliente inválido.");

        lock (_sync)
        {
            var clients = LoadUnlocked();
            var index = clients.FindIndex(item => item.ClientId == clientId);
            var item = new AuthorizedClient(clientId, clientName.Trim(), secret.ToArray(), 0);
            if (index >= 0) clients[index] = item;
            else clients.Add(item);
            SaveUnlocked(clients);
        }
    }

    public bool TryAuthenticateAndAdvance(QueueRequestEnvelope envelope, out string error)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_sync)
        {
            var clients = LoadUnlocked();
            var index = clients.FindIndex(item => item.ClientId == envelope.ClientId);
            if (index < 0)
            {
                error = "Este PC não está autorizado na Central. Faça o pareamento novamente.";
                return false;
            }

            var client = clients[index];
            if (envelope.Sequence <= client.LastSequence)
            {
                error = "Solicitação repetida ou fora de ordem foi bloqueada.";
                return false;
            }

            if (!SharedQueueCrypto.VerifyClientAuthentication(envelope, client.Secret))
            {
                error = "Autenticação do cliente inválida.";
                return false;
            }

            clients[index] = client with { LastSequence = envelope.Sequence };
            SaveUnlocked(clients);
            error = string.Empty;
            return true;
        }
    }

    private List<AuthorizedClient> LoadUnlocked()
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            try
            {
                var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    return JsonSerializer.Deserialize<List<AuthorizedClient>>(plainBytes)?
                        .Where(item => item.ClientId != Guid.Empty && item.Secret is { Length: 32 } && item.LastSequence >= 0)
                        .ToList() ?? [];
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return [];
        }
    }

    private void SaveUnlocked(List<AuthorizedClient> clients)
    {
        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(clients);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Diretório da lista de clientes inválido.");
            Directory.CreateDirectory(directory);
            var temporary = _path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(protectedBytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, _path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private sealed record AuthorizedClient(Guid ClientId, string ClientName, byte[] Secret, long LastSequence);
}

public static class SharedQueuePairingCrypto
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static QueuePairingRequestEnvelope CreateRequest(Guid requestId, QueuePairingRequestPayload payload, byte[] pairingKey)
    {
        ValidateKey(pairingKey);
        var plain = JsonSerializer.SerializeToUtf8Bytes(payload);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plain.Length];
        try
        {
            using var aes = new AesGcm(pairingKey, TagSize);
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(requestId, "pair-request"));
            return new QueuePairingRequestEnvelope(SharedQueueCrypto.ProtocolVersion, requestId, DateTimeOffset.UtcNow, nonce, tag, cipher);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static QueuePairingRequestPayload OpenRequest(QueuePairingRequestEnvelope envelope, byte[] pairingKey)
    {
        ValidateKey(pairingKey);
        ValidateEnvelope(envelope.Version, envelope.RequestId, envelope.Nonce, envelope.Tag, envelope.Ciphertext);
        var plain = new byte[envelope.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(pairingKey, TagSize);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plain, AssociatedData(envelope.RequestId, "pair-request"));
            return JsonSerializer.Deserialize<QueuePairingRequestPayload>(plain)
                ?? throw new CryptographicException("Pedido de pareamento inválido.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static QueuePairingResponseEnvelope CreateResponse(Guid requestId, QueuePairingResponsePayload payload, byte[] pairingKey)
    {
        ValidateKey(pairingKey);
        var plain = JsonSerializer.SerializeToUtf8Bytes(payload);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plain.Length];
        try
        {
            using var aes = new AesGcm(pairingKey, TagSize);
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(requestId, "pair-response"));
            return new QueuePairingResponseEnvelope(SharedQueueCrypto.ProtocolVersion, requestId, DateTimeOffset.UtcNow, nonce, tag, cipher);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static QueuePairingResponsePayload OpenResponse(QueuePairingResponseEnvelope envelope, byte[] pairingKey)
    {
        ValidateKey(pairingKey);
        ValidateEnvelope(envelope.Version, envelope.RequestId, envelope.Nonce, envelope.Tag, envelope.Ciphertext);
        var plain = new byte[envelope.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(pairingKey, TagSize);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plain, AssociatedData(envelope.RequestId, "pair-response"));
            return JsonSerializer.Deserialize<QueuePairingResponsePayload>(plain)
                ?? throw new CryptographicException("Resposta de pareamento inválida.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] AssociatedData(Guid requestId, string direction) =>
        Encoding.UTF8.GetBytes($"nfe-agendamento:v{SharedQueueCrypto.ProtocolVersion}:{requestId:N}:{direction}");

    private static void ValidateKey(byte[] key)
    {
        if (key is null || key.Length != 32)
            throw new CryptographicException("Chave de pareamento inválida.");
    }

    private static void ValidateEnvelope(int version, Guid requestId, byte[] nonce, byte[] tag, byte[] cipher)
    {
        if (version != SharedQueueCrypto.ProtocolVersion || requestId == Guid.Empty)
            throw new CryptographicException("Envelope de pareamento inválido.");
        if (nonce is null || nonce.Length != NonceSize || tag is null || tag.Length != TagSize || cipher is null || cipher.Length == 0)
            throw new CryptographicException("Conteúdo de pareamento inválido.");
    }
}

public sealed class SharedQueuePairingClient
{
    private readonly SharedQueuePaths _paths;
    private readonly ClientPairingStore _store;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _timeout;

    public SharedQueuePairingClient(SharedQueuePaths paths, ClientPairingStore store)
        : this(paths, store, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(30))
    {
    }

    public SharedQueuePairingClient(SharedQueuePaths paths, ClientPairingStore store, TimeSpan pollInterval, TimeSpan timeout)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _pollInterval = pollInterval > TimeSpan.Zero ? pollInterval : throw new ArgumentOutOfRangeException(nameof(pollInterval));
        _timeout = timeout > TimeSpan.Zero ? timeout : throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<PairingResult> PairAsync(string code, CancellationToken cancellationToken = default)
    {
        if (!_paths.ValidateForClient())
            return new PairingResult(false, $"A pasta compartilhada '{SharedQueuePaths.DefaultRoot}' não está disponível.");

        byte[] pairingKey;
        try
        {
            pairingKey = PairingCodeService.DeriveKey(code);
        }
        catch (ArgumentException ex)
        {
            return new PairingResult(false, ex.Message);
        }

        var requestId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var request = SharedQueuePairingCrypto.CreateRequest(
            requestId,
            new QueuePairingRequestPayload(clientId, Environment.MachineName),
            pairingKey);

        try
        {
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request);
            var requestTemp = _paths.PairingRequestTemporaryPath(requestId);
            try
            {
                await SharedQueueFileIO.WriteAtomicAsync(
                    requestTemp,
                    _paths.PairingRequestPath(requestId),
                    requestBytes,
                    SharedQueueFileIO.MaxPairingBytes,
                    overwrite: false,
                    cancellationToken);
            }
            finally
            {
                TryDelete(requestTemp);
            }

            var deadline = DateTimeOffset.UtcNow + _timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var responsePath = _paths.PairingResponsePath(requestId);
                if (File.Exists(responsePath))
                {
                    var bytes = await SharedQueueFileIO.ReadAllBytesAsync(responsePath, SharedQueueFileIO.MaxPairingBytes, cancellationToken);
                    var envelope = JsonSerializer.Deserialize<QueuePairingResponseEnvelope>(bytes)
                        ?? throw new InvalidDataException("Resposta de pareamento vazia.");
                    var payload = SharedQueuePairingCrypto.OpenResponse(envelope, pairingKey);
                    if (payload.ClientId != clientId || payload.ClientSecret is not { Length: 32 } || payload.CentralPublicKey is null || payload.CentralPublicKey.Length == 0)
                        throw new InvalidDataException("Resposta de pareamento não corresponde a este PC.");

                    _store.SavePaired(payload.ClientId, payload.ClientName, payload.ClientSecret, payload.CentralPublicKey, payload.CentralId);
                    TryDelete(responsePath);
                    return new PairingResult(true, "PC pareado com a Central com sucesso.");
                }

                await Task.Delay(_pollInterval, cancellationToken);
            }

            return new PairingResult(false, "Código inválido ou expirado, ou a Central não está disponível para pareamento.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidDataException)
        {
            return new PairingResult(false, $"Não foi possível concluir o pareamento: {ex.Message}");
        }
        finally
        {
            TryDelete(_paths.PairingRequestPath(requestId));
            CryptographicOperations.ZeroMemory(pairingKey);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

public sealed class SharedQueuePairingProcessor
{
    private static readonly TimeSpan MaxRequestAge = TimeSpan.FromMinutes(2);
    private readonly SharedQueuePaths _paths;
    private readonly PairingCodeService _codes;
    private readonly AuthorizedClientStore _authorizedClients;
    private readonly CentralKeyStore _centralKeyStore;

    public SharedQueuePairingProcessor(
        SharedQueuePaths paths,
        PairingCodeService codes,
        AuthorizedClientStore authorizedClients,
        CentralKeyStore centralKeyStore)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _codes = codes ?? throw new ArgumentNullException(nameof(codes));
        _authorizedClients = authorizedClients ?? throw new ArgumentNullException(nameof(authorizedClients));
        _centralKeyStore = centralKeyStore ?? throw new ArgumentNullException(nameof(centralKeyStore));
    }

    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken = default)
    {
        if (!_paths.ValidateForClient() || !_codes.TryGetActiveKey(out var pairingKey))
            return false;

        string? candidate = null;
        try
        {
            candidate = Directory.EnumerateFiles(_paths.PairingDirectory, "*.pair.req", SearchOption.TopDirectoryOnly)
                .OrderBy(File.GetCreationTimeUtc)
                .FirstOrDefault();
            if (candidate is null || !TryParseId(candidate, out var requestId))
                return false;
            if (SharedQueueFileIO.IsReparsePoint(candidate))
                return false;

            var processing = _paths.PairingProcessingPath(requestId);
            try { File.Move(candidate, processing, overwrite: false); }
            catch (IOException) { return false; }

            try
            {
                var bytes = await SharedQueueFileIO.ReadAllBytesAsync(processing, SharedQueueFileIO.MaxPairingBytes, cancellationToken);
                var envelope = JsonSerializer.Deserialize<QueuePairingRequestEnvelope>(bytes)
                    ?? throw new InvalidDataException("Pedido de pareamento vazio.");
                if (envelope.RequestId != requestId)
                    throw new InvalidDataException("Identificador do pareamento inválido.");
                var now = DateTimeOffset.UtcNow;
                if (envelope.CreatedUtc > now.AddMinutes(1) || now - envelope.CreatedUtc > MaxRequestAge)
                    throw new InvalidDataException("Pedido de pareamento expirado.");

                var request = SharedQueuePairingCrypto.OpenRequest(envelope, pairingKey);
                if (request.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(request.ClientName))
                    throw new InvalidDataException("Identidade do cliente inválida.");

                var clientSecret = RandomNumberGenerator.GetBytes(32);
                try
                {
                    _authorizedClients.Authorize(request.ClientId, request.ClientName, clientSecret);
                    var response = SharedQueuePairingCrypto.CreateResponse(
                        requestId,
                        new QueuePairingResponsePayload(
                            request.ClientId,
                            request.ClientName,
                            clientSecret,
                            Environment.MachineName,
                            _centralKeyStore.GetOrCreatePublicKey()),
                        pairingKey);
                    var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response);
                    var responseTemp = _paths.PairingResponseTemporaryPath(requestId);
                    try
                    {
                        await SharedQueueFileIO.WriteAtomicAsync(
                            responseTemp,
                            _paths.PairingResponsePath(requestId),
                            responseBytes,
                            SharedQueueFileIO.MaxPairingBytes,
                            overwrite: true,
                            cancellationToken);
                    }
                    finally
                    {
                        TryDelete(responseTemp);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(clientSecret);
                }

                TryDelete(processing);
                return true;
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException)
            {
                TryDelete(processing);
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pairingKey);
        }
    }

    private static bool TryParseId(string path, out Guid requestId)
    {
        var name = Path.GetFileName(path);
        const string suffix = ".pair.req";
        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            requestId = Guid.Empty;
            return false;
        }
        return Guid.TryParseExact(name[..^suffix.Length], "N", out requestId) && requestId != Guid.Empty;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
