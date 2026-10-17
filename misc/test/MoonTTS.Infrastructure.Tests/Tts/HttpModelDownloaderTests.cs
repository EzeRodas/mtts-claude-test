using System.Net;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.Tts;

namespace MoonTTS.Infrastructure.Tests.Tts;

/* HttpModelDownloader against a dummy HTTP server (a fake HttpMessageHandler): file writing, progress,
   HTTP and network errors, cancellation and temp file cleanup */
public sealed class HttpModelDownloaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeHttpHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly HttpModelDownloader _downloader;
    private readonly string _destination;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /* Every test gets its own destination folder and dummy server */
    public HttpModelDownloaderTests()
    {
        _httpClient = new HttpClient(_handler);
        _downloader = new HttpModelDownloader(_httpClient);
        _destination = _temp.Combine("models", "dummy");
    }

    /* Releases the client and removes the temp folder */
    public void Dispose()
    {
        _httpClient.Dispose();
        _temp.Dispose();
    }

    /* A model whose files are served at https://example.invalid/<name> */
    private static ModelMetadata Model(params string[] files) =>
        new(new ModelId("dummy", "model"), "Model", files.ToDictionary(f => f, f => $"https://example.invalid/{f}"));

    /* Bytes 0..n-1 (mod 256), so content corruption is detectable */
    private static byte[] Payload(int length) => Enumerable.Range(0, length).Select(i => (byte)i).ToArray();

    /* Downloads with a recording progress sink */
    private Task<MoonTTS.Domain.Common.Result> DownloadAsync(ModelMetadata model, RecordingProgress? progress = null, CancellationToken? token = null) =>
        _downloader.DownloadModelAsync(model, _destination, progress, token ?? Ct);

    /* The served bytes are written to the file named by the model */
    [Fact]
    public async Task DownloadModelAsync_SingleFile_WritesFileContent()
    {
        _handler.Serve("a.onnx", Payload(1000));

        var result = await DownloadAsync(Model("a.onnx"));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(Payload(1000), File.ReadAllBytes(Path.Combine(_destination, "a.onnx")));
    }

    /* The file is written through a temporary file that does not remain afterwards */
    [Fact]
    public async Task DownloadModelAsync_SingleFile_LeavesNoTemporaryFile()
    {
        _handler.Serve("a.onnx", Payload(10));

        await DownloadAsync(Model("a.onnx"));

        Assert.Equal(["a.onnx"], Directory.GetFiles(_destination).Select(Path.GetFileName));
    }

    /* The file is requested from its URL */
    [Fact]
    public async Task DownloadModelAsync_SingleFile_RequestsFileUrl()
    {
        _handler.Serve("a.onnx", Payload(10));

        await DownloadAsync(Model("a.onnx"));

        Assert.All(_handler.RequestedUrls, url => Assert.Equal("https://example.invalid/a.onnx", url));
        Assert.NotEmpty(_handler.RequestedUrls);
    }

    /* A missing destination folder is created */
    [Fact]
    public async Task DownloadModelAsync_MissingDestination_CreatesFolder()
    {
        _handler.Serve("a.onnx", Payload(10));

        await DownloadAsync(Model("a.onnx"));

        Assert.True(Directory.Exists(_destination));
    }

    /* A model without files succeeds without any request */
    [Fact]
    public async Task DownloadModelAsync_NoFiles_SucceedsWithoutRequests()
    {
        var result = await DownloadAsync(Model());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Empty(_handler.RequestedUrls);
    }

    /* Every file of a model is written */
    [Fact]
    public async Task DownloadModelAsync_MultipleFiles_WritesEveryFile()
    {
        _handler.Serve("a.onnx", Payload(100));
        _handler.Serve("b.bin", Payload(300));

        await DownloadAsync(Model("a.onnx", "b.bin"));

        Assert.Equal(Payload(100), File.ReadAllBytes(Path.Combine(_destination, "a.onnx")));
        Assert.Equal(Payload(300), File.ReadAllBytes(Path.Combine(_destination, "b.bin")));
    }

    /* Progress is computed over the total size of all files */
    [Fact]
    public async Task DownloadModelAsync_MultipleFiles_ReportsProgressOverAllFiles()
    {
        _handler.Serve("a.onnx", Payload(1000));
        _handler.Serve("b.bin", Payload(3000));
        var progress = new RecordingProgress();

        await DownloadAsync(Model("a.onnx", "b.bin"), progress);

        Assert.Equal([25.0, 100.0], progress.Values);
    }

    /* A file read in several chunks reports increasing progress up to 100 */
    [Fact]
    public async Task DownloadModelAsync_LargeFile_ReportsIncreasingProgressEndingAt100()
    {
        _handler.Serve("a.onnx", Payload(20_000));
        var progress = new RecordingProgress();

        await DownloadAsync(Model("a.onnx"), progress);

        Assert.True(progress.Values.Count > 1);
        Assert.Equal(progress.Values.Order(), progress.Values);
        Assert.Equal(100.0, progress.Values[^1]);
    }

    /* Without Content-Length the total is unknown, so no progress is reported but the file is written */
    [Fact]
    public async Task DownloadModelAsync_UnknownContentLength_WritesFileWithoutProgress()
    {
        _handler.Serve("a.onnx", Payload(500), sendContentLength: false);
        var progress = new RecordingProgress();

        var result = await DownloadAsync(Model("a.onnx"), progress);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Empty(progress.Values);
        Assert.Equal(Payload(500), File.ReadAllBytes(Path.Combine(_destination, "a.onnx")));
    }

    /* Progress reporting is optional */
    [Fact]
    public async Task DownloadModelAsync_NullProgress_Succeeds()
    {
        _handler.Serve("a.onnx", Payload(10));

        var result = await DownloadAsync(Model("a.onnx"), progress: null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    /* An existing file is replaced by the downloaded one */
    [Fact]
    public async Task DownloadModelAsync_ExistingFile_IsReplaced()
    {
        Directory.CreateDirectory(_destination);
        File.WriteAllText(Path.Combine(_destination, "a.onnx"), "old content that is longer than the new one");
        _handler.Serve("a.onnx", Payload(5));

        await DownloadAsync(Model("a.onnx"));

        Assert.Equal(Payload(5), File.ReadAllBytes(Path.Combine(_destination, "a.onnx")));
    }

    /* An HTTP error status yields a failure naming the status code and the file */
    [Fact]
    public async Task DownloadModelAsync_HttpErrorStatus_ReturnsFailureNamingStatusAndFile()
    {
        _handler.Fail("a.onnx", HttpStatusCode.NotFound);

        var result = await DownloadAsync(Model("a.onnx"));

        Assert.True(result.IsFailure);
        Assert.Contains("404", result.ErrorMessage);
        Assert.Contains("a.onnx", result.ErrorMessage);
    }

    /* An HTTP error status writes no file */
    [Fact]
    public async Task DownloadModelAsync_HttpErrorStatus_WritesNoFile()
    {
        _handler.Fail("a.onnx", HttpStatusCode.InternalServerError);

        await DownloadAsync(Model("a.onnx"));

        Assert.Empty(Directory.GetFiles(_destination));
    }

    /* Cancelling mid-download yields a "canceled" failure */
    [Fact]
    public async Task DownloadModelAsync_CancelledMidDownload_ReturnsCanceledFailure()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _handler.ServeThenStall("a.onnx", Payload(100), totalLength: 1000);

        var result = await DownloadAsync(Model("a.onnx"), new RecordingProgress(cts.Cancel), cts.Token);

        Assert.True(result.IsFailure);
        Assert.Contains("canceled", result.ErrorMessage);
    }

    /* Cancelling mid-download removes the partially written temporary file */
    [Fact]
    public async Task DownloadModelAsync_CancelledMidDownload_RemovesTemporaryFile()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        bool temporaryFileExistedWhileDownloading = false;
        var progress = new RecordingProgress(() =>
        {
            temporaryFileExistedWhileDownloading = File.Exists(Path.Combine(_destination, "a.onnx.tmp"));
            cts.Cancel();
        });
        _handler.ServeThenStall("a.onnx", Payload(100), totalLength: 1000);

        await DownloadAsync(Model("a.onnx"), progress, cts.Token);

        Assert.True(temporaryFileExistedWhileDownloading);
        Assert.Empty(Directory.GetFiles(_destination));
    }

    /* An already cancelled token yields a "canceled" failure */
    [Fact]
    public async Task DownloadModelAsync_AlreadyCancelled_ReturnsCanceledFailure()
    {
        _handler.Serve("a.onnx", Payload(10));

        var result = await DownloadAsync(Model("a.onnx"), token: new CancellationToken(canceled: true));

        Assert.True(result.IsFailure);
        Assert.Contains("canceled", result.ErrorMessage);
    }

    /* A network error yields a "network error" failure carrying the HttpRequestException */
    [Fact]
    public async Task DownloadModelAsync_NetworkError_ReturnsNetworkFailure()
    {
        _handler.ThrowNetworkError("a.onnx");

        var result = await DownloadAsync(Model("a.onnx"));

        Assert.True(result.IsFailure);
        Assert.Contains("Network error", result.ErrorMessage);
        Assert.IsType<HttpRequestException>(result.Exception);
    }

    /* A connection dropped mid-body yields a failure and removes the temporary file */
    [Fact]
    public async Task DownloadModelAsync_StreamBreaksMidDownload_ReturnsFailureAndRemovesTemporaryFile()
    {
        _handler.ServeThenBreak("a.onnx", Payload(100), totalLength: 1000);

        var result = await DownloadAsync(Model("a.onnx"));

        Assert.True(result.IsFailure);
        Assert.Contains("Failed to download model files", result.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_destination));
    }

    /* Records every reported value, optionally running an action on the first report */
    private sealed class RecordingProgress(Action? onFirstReport = null) : IProgress<double>
    {
        public List<double> Values { get; } = new();

        /* Synchronous (unlike Progress<T>), so tests can react exactly when progress is reported */
        public void Report(double value)
        {
            Values.Add(value);
            if (Values.Count == 1)
                onFirstReport?.Invoke();
        }
    }

    /* Dummy HTTP server keyed by file name */
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<CancellationToken, HttpResponseMessage>> _routes = new();

        public List<string> RequestedUrls { get; } = new();

        /* Serves the bytes with (or without) a Content-Length header */
        public void Serve(string file, byte[] content, bool sendContentLength = true) =>
            _routes[file] = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = sendContentLength
                    ? new ByteArrayContent(content)
                    : new StreamContent(new ScriptedStream(content, ScriptedStream.AfterData.End))
            };

        /* Answers with an error status */
        public void Fail(string file, HttpStatusCode status) =>
            _routes[file] = _ => new HttpResponseMessage(status) { Content = new ByteArrayContent([]) };

        /* Sends the first bytes, then never sends more (until cancelled) */
        public void ServeThenStall(string file, byte[] firstBytes, long totalLength) =>
            _routes[file] = _ => Partial(firstBytes, totalLength, ScriptedStream.AfterData.Stall);

        /* Sends the first bytes, then drops the connection */
        public void ServeThenBreak(string file, byte[] firstBytes, long totalLength) =>
            _routes[file] = _ => Partial(firstBytes, totalLength, ScriptedStream.AfterData.Break);

        /* Fails the request like an unreachable host */
        public void ThrowNetworkError(string file) =>
            _routes[file] = _ => throw new HttpRequestException("No such host is known.");

        /* A response announcing totalLength bytes but scripted to stop after firstBytes */
        private static HttpResponseMessage Partial(byte[] firstBytes, long totalLength, ScriptedStream.AfterData after)
        {
            var content = new StreamContent(new ScriptedStream(firstBytes, after));
            content.Headers.ContentLength = totalLength;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        /* Routes a request by the last segment of its URL */
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);
            return _routes.TryGetValue(request.RequestUri.Segments[^1], out var route)
                ? Task.FromResult(route(cancellationToken))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /* Non-seekable body that returns its data and then ends, stalls until cancelled, or throws */
    private sealed class ScriptedStream(byte[] data, ScriptedStream.AfterData after) : Stream
    {
        public enum AfterData { End, Stall, Break }

        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        /* Copies the remaining data, then applies the scripted behaviour */
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < data.Length)
            {
                int count = Math.Min(buffer.Length, data.Length - _position);
                data.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            switch (after)
            {
                case AfterData.Stall:
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return 0;
                case AfterData.Break:
                    throw new IOException("The connection was reset by the peer.");
                default:
                    return 0;
            }
        }

        /* Routes the array overload through the Memory overload */
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        /* Synchronous reads are not used by the downloader */
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        /* Read-only stream */
        public override void Flush() { }

        /* Not seekable */
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /* Not seekable */
        public override void SetLength(long value) => throw new NotSupportedException();

        /* Read-only stream */
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
