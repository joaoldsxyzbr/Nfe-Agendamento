using System.Security.Cryptography;
using System.Text;

namespace NfeAgendamento.App.SharedQueue;

public sealed record GroupIdentityTransition(
    byte[] PreviousPublicKeySha256,
    byte[] NewPublicKey,
    byte[] Signature);

public static class GroupRotationProof
{
    private static readonly byte[] Context = Encoding.UTF8.GetBytes("nfe-agendamento:group-identity-transition:v1");

    public static GroupIdentityTransition Create(
        RSA previousPrivateKey,
        byte[] previousPublicKey,
        byte[] newPublicKey)
    {
        ArgumentNullException.ThrowIfNull(previousPrivateKey);
        ValidatePublicKey(previousPublicKey);
        ValidatePublicKey(newPublicKey);

        var previousFingerprint = SHA256.HashData(previousPublicKey);
        var statement = BuildStatement(previousFingerprint, newPublicKey);
        try
        {
            var signature = previousPrivateKey.SignData(
                statement,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
            return new GroupIdentityTransition(
                previousFingerprint,
                newPublicKey.ToArray(),
                signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(statement);
        }
    }

    public static bool VerifyChain(
        byte[] trustedPublicKey,
        byte[] expectedFinalPublicKey,
        IReadOnlyList<GroupIdentityTransition>? transitions)
    {
        ValidatePublicKey(trustedPublicKey);
        ValidatePublicKey(expectedFinalPublicKey);

        if (CryptographicOperations.FixedTimeEquals(trustedPublicKey, expectedFinalPublicKey))
            return true;
        if (transitions is null || transitions.Count == 0)
            return false;

        var current = trustedPublicKey.ToArray();
        var started = false;
        try
        {
            foreach (var transition in transitions)
            {
                if (!started)
                {
                    byte[]? currentFingerprint = null;
                    try
                    {
                        currentFingerprint = SHA256.HashData(current);
                        if (!CryptographicOperations.FixedTimeEquals(
                                currentFingerprint,
                                transition.PreviousPublicKeySha256))
                        {
                            continue;
                        }
                        started = true;
                    }
                    finally
                    {
                        if (currentFingerprint is not null)
                            CryptographicOperations.ZeroMemory(currentFingerprint);
                    }
                }

                if (!VerifyOne(current, transition))
                    return false;

                CryptographicOperations.ZeroMemory(current);
                current = transition.NewPublicKey.ToArray();

                if (CryptographicOperations.FixedTimeEquals(current, expectedFinalPublicKey))
                    return true;
            }

            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(current);
        }
    }

    internal static void ValidateTransition(GroupIdentityTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        if (transition.PreviousPublicKeySha256 is null || transition.PreviousPublicKeySha256.Length != 32)
            throw new CryptographicException("Fingerprint anterior da rotação inválido.");
        ValidatePublicKey(transition.NewPublicKey);
        if (transition.Signature is null || transition.Signature.Length == 0 || transition.Signature.Length > 1024)
            throw new CryptographicException("Assinatura da rotação inválida.");
    }

    private static bool VerifyOne(byte[] currentPublicKey, GroupIdentityTransition transition)
    {
        try
        {
            ValidateTransition(transition);
            var currentFingerprint = SHA256.HashData(currentPublicKey);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(currentFingerprint, transition.PreviousPublicKeySha256))
                    return false;

                var statement = BuildStatement(transition.PreviousPublicKeySha256, transition.NewPublicKey);
                try
                {
                    using var rsa = RSA.Create();
                    rsa.ImportSubjectPublicKeyInfo(currentPublicKey, out _);
                    return rsa.VerifyData(
                        statement,
                        transition.Signature,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pss);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(statement);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(currentFingerprint);
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static byte[] BuildStatement(byte[] previousFingerprint, byte[] newPublicKey)
    {
        var newFingerprint = SHA256.HashData(newPublicKey);
        try
        {
            var statement = new byte[Context.Length + 1 + previousFingerprint.Length + newFingerprint.Length + newPublicKey.Length];
            var offset = 0;
            Buffer.BlockCopy(Context, 0, statement, offset, Context.Length);
            offset += Context.Length;
            statement[offset++] = 0;
            Buffer.BlockCopy(previousFingerprint, 0, statement, offset, previousFingerprint.Length);
            offset += previousFingerprint.Length;
            Buffer.BlockCopy(newFingerprint, 0, statement, offset, newFingerprint.Length);
            offset += newFingerprint.Length;
            Buffer.BlockCopy(newPublicKey, 0, statement, offset, newPublicKey.Length);
            return statement;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(newFingerprint);
        }
    }

    private static void ValidatePublicKey(byte[] publicKey)
    {
        if (publicKey is null || publicKey.Length == 0 || publicKey.Length > 4096)
            throw new CryptographicException("Chave pública da identidade do grupo inválida.");
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(publicKey, out _);
    }
}
