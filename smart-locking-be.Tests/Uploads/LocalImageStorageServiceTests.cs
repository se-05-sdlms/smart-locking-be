using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using smart_locking_be.API.Services;

namespace smart_locking_be.Tests.Uploads;

public sealed class LocalImageStorageServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "smart-locking-upload-tests", Guid.NewGuid().ToString("N"));
    private static readonly byte[] PngHeader = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];

    [Fact]
    public async Task Upload_uses_generated_name_and_preserves_bytes()
    {
        var service = new LocalImageStorageService(new EnvironmentStub(root));
        string url = await service.UploadAsync(new MemoryStream(PngHeader), "../../evidence.png", "image/png");
        Assert.Matches("^/uploads/images/[a-f0-9]{32}\\.png$", url);
        Assert.Equal(PngHeader, await File.ReadAllBytesAsync(Path.Combine(root, "wwwroot", url.TrimStart('/'))));
    }

    [Theory]
    [InlineData("image.jpg", "image/jpeg")]
    [InlineData("image.svg", "image/svg+xml")]
    [InlineData("image.png", "image/jpeg")]
    public async Task Upload_rejects_signature_or_media_type_mismatch(string name, string contentType)
    {
        var service = new LocalImageStorageService(new EnvironmentStub(root));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(new MemoryStream(PngHeader), name, contentType));
        Assert.False(Directory.Exists(Path.Combine(root, "wwwroot", "uploads")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
