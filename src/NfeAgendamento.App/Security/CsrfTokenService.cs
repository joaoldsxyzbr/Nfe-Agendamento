using System.Security.Cryptography;

namespace NfeAgendamento.App.Security;

public sealed class CsrfTokenService
{
    private readonly byte[] _tokenBytes = RandomNumberGenerator.GetBytes(32);

    public string CurrentToken => Convert.ToHexString(_tokenBytes);

    public bool Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        byte[] candidate;
        try
        {
            candidate = Convert.FromHexString(token);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(_tokenBytes, candidate);
    }
}
