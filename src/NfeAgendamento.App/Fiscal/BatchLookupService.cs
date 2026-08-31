using System.IO.Compression;
using System.Text;

namespace NfeAgendamento.App.Fiscal;

public sealed record BatchLookupItem(
    string AccessKey,
    NfeLookupStatus Status,
    string? Xml,
    string? Message);

public sealed record BatchLookupResult(IReadOnlyList<BatchLookupItem> Items);

public sealed class BatchLookupService
{
    public const int MaxKeys = 100;
    private readonly NfeLookupService _lookup;

    public BatchLookupService(NfeLookupService lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public async Task<BatchLookupResult> LookupAsync(
        IEnumerable<string> accessKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessKeys);

        var keys = accessKeys
            .Select(key => key?.Trim() ?? string.Empty)
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (keys.Length == 0)
            throw new ArgumentException("Informe pelo menos uma chave NF-e.", nameof(accessKeys));
        if (keys.Length > MaxKeys)
            throw new ArgumentException($"O lote pode conter no máximo {MaxKeys} chaves.", nameof(accessKeys));
        if (keys.Any(key => !AccessKeyValidator.IsValid(key)))
            throw new ArgumentException("Todas as chaves do lote devem ter 44 dígitos válidos.", nameof(accessKeys));

        var items = new List<BatchLookupItem>(keys.Length);
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _lookup.LookupAsync(key, cancellationToken);
            items.Add(new BatchLookupItem(key, result.Status, result.Xml, result.Message));

            if (result.Status == NfeLookupStatus.Blocked)
            {
                foreach (var remaining in keys.Skip(items.Count))
                    items.Add(new BatchLookupItem(remaining, NfeLookupStatus.Blocked, null, result.Message));
                break;
            }
        }

        return new BatchLookupResult(items);
    }

    public static byte[] CreateZip(BatchLookupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in result.Items.Where(item => item.Status == NfeLookupStatus.Found && item.Xml is not null))
            {
                var entry = archive.CreateEntry($"{item.AccessKey}.xml", CompressionLevel.Fastest);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(item.Xml);
            }

            var summary = archive.CreateEntry("resultado.txt", CompressionLevel.Fastest);
            using var summaryWriter = new StreamWriter(summary.Open(), new UTF8Encoding(false));
            summaryWriter.WriteLine($"Total de chaves: {result.Items.Count}");
            summaryWriter.WriteLine($"Encontradas: {result.Items.Count(item => item.Status == NfeLookupStatus.Found)}");
            summaryWriter.WriteLine($"Não encontradas: {result.Items.Count(item => item.Status == NfeLookupStatus.NotFound)}");
            summaryWriter.WriteLine();
            foreach (var item in result.Items.Where(item => item.Status != NfeLookupStatus.Found))
                summaryWriter.WriteLine($"{item.AccessKey}: {item.Message ?? item.Status.ToString()}");
        }

        return output.ToArray();
    }
}
