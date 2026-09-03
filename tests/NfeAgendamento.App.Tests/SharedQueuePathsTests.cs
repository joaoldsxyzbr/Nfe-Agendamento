using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueuePathsTests
{
    [Fact]
    public void Production_root_is_the_dedicated_folder()
    {
        Assert.Equal(@"P:\01-Nfe agendamento", SharedQueuePaths.DefaultRoot);
    }

    [Fact]
    public void Request_paths_never_escape_the_configured_root()
    {
        var root = NewTempRoot();
        var paths = new SharedQueuePaths(root);
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var request = Path.GetFullPath(paths.RequestPath(id));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        Assert.StartsWith(normalizedRoot, request, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("fila", "11111111111111111111111111111111.req"), request, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"P:\outro")]
    [InlineData("heartbeat/../../fora")]
    public void Arbitrary_status_names_are_rejected(string value)
    {
        var paths = new SharedQueuePaths(NewTempRoot());
        Assert.Throws<ArgumentException>(() => paths.StatusPath(value));
    }

    [Fact]
    public void Central_initialization_requires_existing_root_and_does_not_create_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-agendamento-tests", Guid.NewGuid().ToString("N"));
        var paths = new SharedQueuePaths(root);

        Assert.Throws<DirectoryNotFoundException>(() => paths.InitializeAsCentral());
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Central_initialization_creates_only_expected_structure_and_marker()
    {
        var root = NewTempRoot(create: true);
        try
        {
            var paths = new SharedQueuePaths(root);
            paths.InitializeAsCentral();

            Assert.Equal(SharedQueuePaths.MarkerContents, File.ReadAllText(paths.MarkerPath));
            Assert.True(Directory.Exists(paths.QueueDirectory));
            Assert.True(Directory.Exists(paths.ProcessingDirectory));
            Assert.True(Directory.Exists(paths.ResponsesDirectory));
            Assert.True(Directory.Exists(paths.StatusDirectory));
            Assert.True(Directory.Exists(paths.PairingDirectory));
            Assert.True(Directory.Exists(paths.CandidatesDirectory));

            var names = Directory.EnumerateFileSystemEntries(root)
                .Select(Path.GetFileName)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.Equal(
                new[] { ".nfe-agendamento", "candidatos", "fila", "pareamento", "processando", "respostas", "status" }.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
                names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Client_validation_rejects_missing_or_invalid_marker()
    {
        var root = NewTempRoot(create: true);
        try
        {
            var paths = new SharedQueuePaths(root);
            Assert.False(paths.ValidateForClient());

            File.WriteAllText(paths.MarkerPath, "arquivo-qualquer");
            Assert.False(paths.ValidateForClient());

            paths.InitializeAsCentral();
            Assert.True(paths.ValidateForClient());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempRoot(bool create = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-agendamento-tests", Guid.NewGuid().ToString("N"));
        if (create)
            Directory.CreateDirectory(root);
        return root;
    }
}
