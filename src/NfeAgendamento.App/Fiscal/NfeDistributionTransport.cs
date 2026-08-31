using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NfeAgendamento.App.Fiscal;

public sealed class NfeDistributionTransport : INfeDistributionTransport, IDisposable
{
    private const string Endpoint = "https://www1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx";
    private readonly HttpClient _httpClient;
    private readonly X509Certificate2 _certificate;
    private readonly string _cnpj;
    private readonly string _ufAutor;

    public NfeDistributionTransport(X509Certificate2 certificate, string cnpj, string ufAutor)
    {
        _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        _cnpj = ValidateDigits(cnpj, 14, "CNPJ");
        _ufAutor = ValidateDigits(ufAutor, 2, "UF autora");

        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(_certificate);
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
    }

    public async Task<NfeDistributionResponse> QueryByAccessKeyAsync(
        string accessKey,
        CancellationToken cancellationToken = default)
    {
        var soap = NfeDistributionProtocol.BuildSoap(accessKey, _cnpj, _ufAutor);
        using var content = new StringContent(soap, Encoding.UTF8, "text/xml");
        content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");

        using var response = await _httpClient.PostAsync(Endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limited = new LimitedReadStream(stream, 10 * 1024 * 1024);
        using var reader = new StreamReader(limited, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var body = await reader.ReadToEndAsync(cancellationToken);

        return NfeDistributionProtocol.ParseResponse(body, accessKey);
    }

    public void Dispose() => _httpClient.Dispose();

    private static string ValidateDigits(string value, int length, string label)
    {
        if (value.Length != length || value.Any(c => c is < '0' or > '9'))
            throw new ArgumentException($"{label} inválido.", label);
        return value;
    }

    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private long _read;

        public LimitedReadStream(Stream inner, long limit)
        {
            _inner = inner;
            _limit = limit;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _read;
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var allowed = (int)Math.Min(buffer.Length, _limit - _read + 1);
            if (allowed <= 0) throw new InvalidDataException("Resposta da SEFAZ excede o limite permitido.");
            var read = _inner.Read(buffer[..allowed]);
            _read += read;
            if (_read > _limit) throw new InvalidDataException("Resposta da SEFAZ excede o limite permitido.");
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var allowed = (int)Math.Min(buffer.Length, _limit - _read + 1);
            if (allowed <= 0) throw new InvalidDataException("Resposta da SEFAZ excede o limite permitido.");
            var read = await _inner.ReadAsync(buffer[..allowed], cancellationToken);
            _read += read;
            if (_read > _limit) throw new InvalidDataException("Resposta da SEFAZ excede o limite permitido.");
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
