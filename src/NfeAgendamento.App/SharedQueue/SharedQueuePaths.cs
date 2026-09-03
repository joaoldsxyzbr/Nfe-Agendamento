using System.Text;

namespace NfeAgendamento.App.SharedQueue;

public sealed class SharedQueuePaths
{
    public const string DefaultRoot = @"P:\01-Nfe agendamento";
    public const string MarkerContents = "nfe-agendamento-share-v1";

    private static readonly HashSet<string> AllowedStatusFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "central.lock",
        "heartbeat.json",
        "group-identity.bin",
        "authorized-clients.bin"
    };

    private readonly string _rootWithSeparator;

    public SharedQueuePaths(string? rootOverride = null)
    {
        var requestedRoot = string.IsNullOrWhiteSpace(rootOverride) ? DefaultRoot : rootOverride;
        Root = Path.GetFullPath(requestedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(Root))
            throw new ArgumentException("Raiz da fila compartilhada inválida.", nameof(rootOverride));

        _rootWithSeparator = Root + Path.DirectorySeparatorChar;
    }

    public string Root { get; }
    public string QueueDirectory => ChildDirectory("fila");
    public string ProcessingDirectory => ChildDirectory("processando");
    public string ResponsesDirectory => ChildDirectory("respostas");
    public string StatusDirectory => ChildDirectory("status");
    public string PairingDirectory => ChildDirectory("pareamento");
    public string CandidatesDirectory => ChildDirectory("candidatos");
    public string MarkerPath => EnsureInsideRoot(Path.Combine(Root, ".nfe-agendamento"));
    public string GroupIdentityPath => StatusPath("group-identity.bin");
    public string AuthorizedClientsPath => StatusPath("authorized-clients.bin");

    public string RequestPath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(QueueDirectory, $"{ValidateId(requestId):N}.req"));

    public string RequestTemporaryPath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(QueueDirectory, $"{ValidateId(requestId):N}.req.tmp"));

    public string ProcessingPath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(ProcessingDirectory, $"{ValidateId(requestId):N}.req"));

    public string ResponsePath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(ResponsesDirectory, $"{ValidateId(requestId):N}.res"));

    public string ResponseTemporaryPath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(ResponsesDirectory, $"{ValidateId(requestId):N}.res.tmp"));

    public string PairingRequestPath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(PairingDirectory, $"{ValidateId(requestId):N}.pair.req"));

    public string PairingRequestTemporaryPath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(PairingDirectory, $"{ValidateId(requestId):N}.pair.req.tmp"));

    public string PairingProcessingPath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(PairingDirectory, $"{ValidateId(requestId):N}.pair.processing"));

    public string PairingResponsePath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(PairingDirectory, $"{ValidateId(requestId):N}.pair.res"));

    public string PairingResponseTemporaryPath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(PairingDirectory, $"{ValidateId(requestId):N}.pair.res.tmp"));

    public string CandidateBundlePath(Guid clientId) =>
        EnsureInsideRoot(Path.Combine(CandidatesDirectory, $"{ValidateId(clientId):N}.candidate"));

    public string CandidateBundleTemporaryPath(Guid clientId, Guid writeId) =>
        EnsureInsideRoot(Path.Combine(CandidatesDirectory, $"{ValidateId(clientId):N}.{ValidateId(writeId):N}.candidate.tmp"));

    public string HeartbeatTemporaryPath(Guid writeId) =>
        EnsureInsideRoot(Path.Combine(StatusDirectory, $"heartbeat.{ValidateId(writeId):N}.tmp"));

    public string GroupIdentityTemporaryPath(Guid writeId) =>
        EnsureInsideRoot(Path.Combine(StatusDirectory, $"group-identity.{ValidateId(writeId):N}.tmp"));

    public string AuthorizedClientsTemporaryPath(Guid writeId) =>
        EnsureInsideRoot(Path.Combine(StatusDirectory, $"authorized-clients.{ValidateId(writeId):N}.tmp"));

    public string StatusPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !AllowedStatusFiles.Contains(fileName)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Arquivo de status não permitido.", nameof(fileName));
        }

        return EnsureInsideRoot(Path.Combine(StatusDirectory, fileName));
    }

    public void InitializeAsCentral()
    {
        if (!Directory.Exists(Root))
            throw new DirectoryNotFoundException($"A pasta compartilhada '{Root}' não está disponível.");

        SharedQueueFileIO.EnsureNotReparsePoint(Root);
        EnsureDirectory(QueueDirectory);
        EnsureDirectory(ProcessingDirectory);
        EnsureDirectory(ResponsesDirectory);
        EnsureDirectory(StatusDirectory);
        EnsureDirectory(PairingDirectory);
        EnsureDirectory(CandidatesDirectory);

        if (File.Exists(MarkerPath))
            SharedQueueFileIO.EnsureNotReparsePoint(MarkerPath);

        var temporary = EnsureInsideRoot(Path.Combine(Root, $".nfe-agendamento.{Guid.NewGuid():N}.tmp"));
        var bytes = Encoding.UTF8.GetBytes(MarkerContents);
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 256,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, MarkerPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public bool ValidateForClient()
    {
        try
        {
            if (!Directory.Exists(Root)
                || !File.Exists(MarkerPath)
                || !Directory.Exists(QueueDirectory)
                || !Directory.Exists(ProcessingDirectory)
                || !Directory.Exists(ResponsesDirectory)
                || !Directory.Exists(StatusDirectory)
                || !Directory.Exists(PairingDirectory))
            {
                return false;
            }

            SharedQueueFileIO.EnsureNotReparsePoint(Root);
            SharedQueueFileIO.EnsureNotReparsePoint(QueueDirectory);
            SharedQueueFileIO.EnsureNotReparsePoint(ProcessingDirectory);
            SharedQueueFileIO.EnsureNotReparsePoint(ResponsesDirectory);
            SharedQueueFileIO.EnsureNotReparsePoint(StatusDirectory);
            SharedQueueFileIO.EnsureNotReparsePoint(PairingDirectory);
            if (Directory.Exists(CandidatesDirectory))
                SharedQueueFileIO.EnsureNotReparsePoint(CandidatesDirectory);
            SharedQueueFileIO.EnsureNotReparsePoint(MarkerPath);

            var markerBytes = SharedQueueFileIO.ReadAllBytes(MarkerPath, SharedQueueFileIO.MaxMarkerBytes);
            return string.Equals(
                Encoding.UTF8.GetString(markerBytes).Trim(),
                MarkerContents,
                StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or InvalidDataException
            or NotSupportedException)
        {
            return false;
        }
    }

    private void EnsureDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            SharedQueueFileIO.EnsureNotReparsePoint(path);
            return;
        }

        Directory.CreateDirectory(path);
        SharedQueueFileIO.EnsureNotReparsePoint(path);
    }

    private string ChildDirectory(string name) =>
        EnsureInsideRoot(Path.Combine(Root, name));

    private string EnsureInsideRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho fora da pasta dedicada do NFe Agendamento.");

        return fullPath;
    }

    private static Guid ValidateId(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Identificador de arquivo inválido.", nameof(id));
        return id;
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
