using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using smart_locking_be.API.Services;

namespace smart_locking_be.Tests.Storage;

public sealed class LocalImageStorageServiceTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3, 4, 5, 6, 7];
    [Fact]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_UTC01()
    {
        using var c = new Context(); using var stream = new MemoryStream(Png);
        var url = await c.Service.UploadAsync(stream, "../../photo.PNG", "image/png");
        Assert.Matches(@"^/uploads/images/[0-9a-f]{32}\.png$", url);
        Assert.Equal(Png, await File.ReadAllBytesAsync(c.FileFor(url)));
        Assert.Single(Directory.GetFiles(c.Root, "*", SearchOption.AllDirectories));
    }
    [Fact]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_UTC02()
    {
        using var c = new Context(); using var stream = new MemoryStream(Png); var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.UploadAsync(stream, "photo.exe", "image/png")); Assert.Equal("Ảnh phải là JPG, PNG hoặc WEBP.", ex.Message); Assert.Empty(Directory.GetFiles(c.Root, "*", SearchOption.AllDirectories));
    }
    [Fact]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_UTC03()
    {
        using var c = new Context(); using var stream = new MemoryStream([1, 2, 3]); var ex = await Assert.ThrowsAsync<ArgumentException>(() => c.Service.UploadAsync(stream, "photo.png", "image/png")); Assert.Equal("Nội dung tệp không đúng định dạng ảnh.", ex.Message); Assert.Empty(Directory.GetFiles(c.Root, "*", SearchOption.AllDirectories));
    }
    [Fact(Skip = "Excel IMG-01 UTC04 gives null stream/empty filename/MIME without an expected exception. Clarify validation order/contract.")]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_UTC04()
    { using var c = new Context(); Assert.Null(await c.Service.UploadAsync(null!, "", "")); }

    [Theory]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "IMAGE/JPEG")]
    [InlineData("photo.webp", "image/webp")]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_AcceptsSupportedSignatures(string filename, string mime)
    {
        byte[] bytes = filename.EndsWith("webp", StringComparison.Ordinal) ? [82, 73, 70, 70, 0, 0, 0, 0, 87, 69, 66, 80, 9] : [255, 216, 255, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        using var c = new Context(); using var stream = new MemoryStream(bytes); var url = await c.Service.UploadAsync(stream, filename, mime); Assert.Equal(bytes, await File.ReadAllBytesAsync(c.FileFor(url))); Assert.EndsWith(Path.GetExtension(filename), url);
    }
    [Fact]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_PreservesEntireNonSeekableStreamAndGeneratesUniqueNames()
    {
        using var c = new Context(); using var first = new NonSeekableStream(Png); using var second = new MemoryStream(Png);
        var url = await c.Service.UploadAsync(first, "photo.png", "image/png"); var other = await c.Service.UploadAsync(second, "photo.png", "image/png");
        Assert.NotEqual(url, other); Assert.Equal(Png, await File.ReadAllBytesAsync(c.FileFor(url))); Assert.Equal(Png, await File.ReadAllBytesAsync(c.FileFor(other)));
    }
    [Fact]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_RejectsMismatchedMimeAndHonorsCancellationBeforeWriting()
    {
        using var c = new Context(); using var stream = new MemoryStream(Png); await Assert.ThrowsAsync<ArgumentException>(() => c.Service.UploadAsync(stream, "photo.png", "image/jpeg"));
        using var source = new CancellationTokenSource(); source.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c.Service.UploadAsync(stream, "photo.png", "image/png", source.Token)); Assert.Empty(Directory.GetFiles(c.Root, "*", SearchOption.AllDirectories));
    }
    private sealed class Context : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "sdlms-ut-image-" + Guid.NewGuid().ToString("N"));
        public LocalImageStorageService Service { get; }
        public Context() { Directory.CreateDirectory(Root); Service = new(new EnvironmentStub { ContentRootPath = Root, WebRootPath = Path.Combine(Root, "wwwroot") }); }
        public string FileFor(string url) => Path.Combine(Root, "wwwroot", url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root); var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("sdlms-ut-image-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test directory.");
            Directory.Delete(resolved, true);
        }
    }
    private sealed class EnvironmentStub : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = "";
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
