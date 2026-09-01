using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace NfeAgendamento.App.Security;

public sealed class LocalSessionService
{
    public const string CookieName = "nfe_agendamento_session";
    public const string DefaultPassword = "agendamentoprado";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 120_000;
    private static readonly byte[] Entropy = "NfeAgendamento.Auth.v1"u8.ToArray();
    private readonly string _path;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new(StringComparer.Ordinal);
    private readonly object _fileGate = new();

    public LocalSessionService()
        : this(Path.Combine(AppPaths.StateRoot, "auth.bin"))
    {
    }

    public LocalSessionService(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Caminho de autenticação inválido.", nameof(path))
            : path;
    }

    public bool IsConfigured => true;

    public void Configure(string password)
    {
        ValidatePassword(password);
        lock (_fileGate)
        {
            if (IsConfigured)
                throw new InvalidOperationException("A senha local já foi configurada.");

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Hash(password, salt);
            var payload = JsonSerializer.SerializeToUtf8Bytes(new PasswordRecord(salt, hash));
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path, ProtectedData.Protect(payload, Entropy, DataProtectionScope.CurrentUser));
        }
    }

    public bool Verify(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        if (string.Equals(password, DefaultPassword, StringComparison.Ordinal))
            return true;

        if (!File.Exists(_path))
            return CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(password),
                System.Text.Encoding.UTF8.GetBytes(DefaultPassword));

        PasswordRecord record;
        lock (_fileGate)
        {
            try
            {
                var payload = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
                record = JsonSerializer.Deserialize<PasswordRecord>(payload)
                    ?? throw new InvalidDataException("Credencial local inválida.");
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
            {
                throw new InvalidDataException("A credencial local não pôde ser validada com segurança.", ex);
            }
        }

        var candidate = Hash(password, record.Salt);
        return CryptographicOperations.FixedTimeEquals(candidate, record.Hash);
    }

    public string CreateSession()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = DateTimeOffset.UtcNow.AddHours(8);
        return token;
    }

    public bool IsAuthenticated(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !_sessions.TryGetValue(token, out var expiresAt))
            return false;
        if (expiresAt > DateTimeOffset.UtcNow)
            return true;
        _sessions.TryRemove(token, out _);
        return false;
    }

    public void Revoke(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            _sessions.TryRemove(token, out _);
    }

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

    private static void ValidatePassword(string password)
    {
        if (password.Length != 6 || password.Any(character => character is < '0' or > '9'))
            throw new ArgumentException("A senha deve conter exatamente 6 números.", nameof(password));
    }

    private sealed record PasswordRecord(byte[] Salt, byte[] Hash);
}
