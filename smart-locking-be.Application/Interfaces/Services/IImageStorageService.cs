namespace smart_locking_be.Application.Interfaces.Services;

public interface IImageStorageService
{
    Task<string> UploadAsync(
        Stream image,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
