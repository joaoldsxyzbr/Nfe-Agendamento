using Sigstore;

namespace NfeAgendamento.App.Updates;

public interface IUpdateSignatureVerifier
{
    Task VerifyAsync(
        string packagePath,
        string bundleJson,
        CancellationToken cancellationToken = default);
}

public sealed class SigstoreUpdateSignatureVerifier : IUpdateSignatureVerifier
{
    public const string GitHubOidcIssuer = "https://token.actions.githubusercontent.com";
    public const string RepositoryUri = "https://github.com/joaoldsxyzbr/Nfe-Agendamento";
    public const string ReleaseWorkflowIdentity =
        "https://github.com/joaoldsxyzbr/Nfe-Agendamento/.github/workflows/release-bridge.yml@refs/heads/main";

    public static VerificationPolicy CreatePolicy() =>
        new()
        {
            CertificateIdentity = new CertificateIdentity
            {
                SubjectAlternativeName = ReleaseWorkflowIdentity,
                Issuer = GitHubOidcIssuer,
                Extensions = new CertificateExtensionPolicy
                {
                    SourceRepositoryUri = RepositoryUri,
                    SourceRepositoryRef = "refs/heads/main",
                    RunnerEnvironment = "github-hosted",
                    SourceRepositoryVisibilityAtSigning = "public"
                }
            },
            RequireTransparencyLog = true,
            TransparencyLogThreshold = 1,
            RequireSignedCertificateTimestamps = true
        };

    public async Task VerifyAsync(
        string packagePath,
        string bundleJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Caminho do pacote de atualização inválido.", nameof(packagePath));
        if (string.IsNullOrWhiteSpace(bundleJson))
            throw new InvalidDataException("Bundle Sigstore de atualização vazio.");

        try
        {
            var bundle = SigstoreBundle.Deserialize(bundleJson);
            var verifier = new SigstoreVerifier();
            await using var artifact = File.OpenRead(packagePath);
            await verifier.VerifyStreamAsync(
                artifact,
                bundle,
                CreatePolicy(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "A assinatura Sigstore da atualização não pôde ser validada contra o workflow oficial.",
                ex);
        }
    }
}
