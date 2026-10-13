using Microsoft.AspNetCore.Hosting;
using smart_locking_be.API.Services;

namespace smart_locking_be.Tests.Uploads;

public sealed class LocalImageStorageServiceTests
{
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "smart-locking-be";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
    }

    #region IMG-01: UploadAsync
    [Fact]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_UTC01()
    {
        var env = new TestWebHostEnvironment();
        var service = new LocalImageStorageService(env);

        // PNG signature: 89 50 4E 47 0D 0A 1A 0A + extra bytes
        byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x12, 0x34];
        using var stream = new MemoryStream(pngBytes);

        string result = await service.UploadAsync(stream, "test.png", "image/png");

        Assert.StartsWith("/uploads/images/", result);
        Assert.EndsWith(".png", result);
    }

    [Fact]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_UTC02()
    {
        var env = new TestWebHostEnvironment();
        var service = new LocalImageStorageService(env);
        using var stream = new MemoryStream([0x01, 0x02]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadAsync(stream, "test.exe", "application/octet-stream"));

        Assert.Equal("Ảnh phải là JPG, PNG hoặc WEBP.", ex.Message);
    }

    [Fact]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_UTC03()
    {
        var env = new TestWebHostEnvironment();
        var service = new LocalImageStorageService(env);
        // fake jpg extension but invalid magic bytes
        using var stream = new MemoryStream([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadAsync(stream, "fake.jpg", "image/jpeg"));

        Assert.Equal("Nội dung tệp không đúng định dạng ảnh.", ex.Message);
    }

    [Fact]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_UTC04()
    {
        var env = new TestWebHostEnvironment();
        var service = new LocalImageStorageService(env);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.UploadAsync(null!, "", ""));
    }

    [Fact]
    [Trait("Category", "IMG-01")]
    public async Task UploadAsync_UTC05()
    {
        var env = new TestWebHostEnvironment();
        var service = new LocalImageStorageService(env);
        // WEBP signature: RIFF (4 bytes), size (4 bytes), WEBP (4 bytes)
        byte[] webpBytes = [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x00, 0x00];
        using var stream = new MemoryStream(webpBytes);

        string result = await service.UploadAsync(stream, "parcel.webp", "image/webp");

        Assert.StartsWith("/uploads/images/", result);
        Assert.EndsWith(".webp", result);
    }
    #endregion
}
