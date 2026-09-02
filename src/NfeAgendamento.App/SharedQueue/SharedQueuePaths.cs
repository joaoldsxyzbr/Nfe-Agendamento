namespace NfeAgendamento.App.SharedQueue;

public sealed class SharedQueuePaths
{
    public const string DefaultRoot = @"P:\01-Nfe agendamento";
    public const string MarkerContents = "nfe-agendamento-share-v1";

    private static readonly HashSet<string> AllowedStatusFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "central.lock",
        "heartbeat.json"
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
    public string MarkerPath => EnsureInsideRoot(Path.Combine(Root, ".nfe-agendamento"));

    public string RequestPath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(QueueDirectory, $"{requestId:N}.req"));

    public string ProcessingPath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(ProcessingDirectory, $"{requestId:N}.req"));

    public string ResponsePath(Guid requestId) =>
        EnsureInsideRoot(Path.Combine(ResponsesDirectory, $"{requestId:N}.res"));

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
            throw new DirectoryNotFoundException($"A pasta compartilhada '{DefaultRoot}' não está disponível.");

        Directory.CreateDirectory(QueueDirectory);
        Directory.CreateDirectory(ProcessingDirectory);
        Directory.CreateDirectory(ResponsesDirectory);
        Directory.CreateDirectory(StatusDirectory);

        var temporary = MarkerPath + ".tmp";
        File.WriteAllText(temporary, MarkerContents);
        File.Move(temporary, MarkerPath, overwrite: true);
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
                || !Directory.Exists(StatusDirectory))
            {
                return false;
            }

            return string.Equals(
                File.ReadAllText(MarkerPath).Trim(),
                MarkerContents,
                StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
}
