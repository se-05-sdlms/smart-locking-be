using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Controllers;
using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Application.Interfaces.Services;
using Xunit;

namespace smart_locking_be.Tests.API;

public sealed class WaybillValidationTests
{
    private const int FiveMiB = 5 * 1024 * 1024;

    private sealed class FakeDeliveryRequestService : IDeliveryRequestService
    {
        public bool ScanWaybillCalled { get; private set; }
        public long ScannedStreamLength { get; private set; }

        public Task<WaybillOcrResponse> ScanWaybillAsync(
            Guid id,
            string guestSessionToken,
            Stream image,
            string fileName,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            ScanWaybillCalled = true;
            ScannedStreamLength = image.Length;
            return Task.FromResult(new WaybillOcrResponse(
                Status: "Succeeded",
                Phones: new[] { "0901234567" },
                IsMasked: false,
                ParcelImageUrl: "https://example.test/waybill.jpg"));
        }

        public Task<InitiateDeliveryResponse> CreateAsync(InitiateDeliveryRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DeliveryRequestSummaryResponse> SubmitAsync(Guid id, string guestSessionToken, SubmitDeliveryRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ResolveRecipientResponse> ResolveRecipientAsync(Guid id, string guestSessionToken, ResolveRecipientRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GuestDeliveryStatusResponse> GetAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> ExpireStartedSessionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DeliveryRequestSummaryResponse> CancelAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ResidentDeliveryRequestResponse> GetForResidentAsync(Guid residentUserId, Guid requestId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<PendingDeliveryRequestResponse>> GetPendingRequestsForResidentAsync(Guid residentUserId, CancellationToken cancellationToken = default, int pageNumber = 1, int pageSize = 20) => throw new NotImplementedException();
        public Task<DeliveryRequestSummaryResponse> ApproveDeliveryRequestAsync(Guid residentUserId, Guid requestId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DeliveryRequestSummaryResponse> RejectDeliveryRequestAsync(Guid residentUserId, Guid requestId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> ExpirePendingApprovalsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CompartmentReservationResponse> OpenCompartmentAsync(Guid requestId, string guestSessionToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> ExpireReservationsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task FinalizeDropOffAsync(Guid requestId, DateTimeOffset completedAt, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static (DeliveryRequestsController Controller, FakeDeliveryRequestService Service) CreateController(
        string? guestSessionToken = "valid-guest-token")
    {
        var service = new FakeDeliveryRequestService();
        var controller = new DeliveryRequestsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        if (!string.IsNullOrEmpty(guestSessionToken))
        {
            controller.HttpContext.Request.Headers["X-Guest-Session-Token"] = guestSessionToken;
        }

        return (controller, service);
    }

    private static IFormFile CreateMockFormFile(long length, string fileName = "waybill.jpg", string contentType = "image/jpeg")
    {
        var stream = new MemoryStream(new byte[Math.Min(length, 1024)]);
        return new DummyFormFile(stream, length, fileName, contentType);
    }

    private sealed class DummyFormFile(Stream stream, long length, string fileName, string contentType) : IFormFile
    {
        public string ContentType => contentType;
        public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{fileName}\"";
        public IHeaderDictionary Headers => new HeaderDictionary();
        public long Length => length;
        public string Name => "file";
        public string FileName => fileName;

        public void CopyTo(Stream target) => stream.CopyTo(target);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => stream.CopyToAsync(target, cancellationToken);
        public Stream OpenReadStream() => stream;
    }

    [Fact]
    public async Task ScanWaybill_MissingGuestSessionToken_ThrowsUnauthorizedException()
    {
        var (controller, _) = CreateController(guestSessionToken: null);
        var file = CreateMockFormFile(1024);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            controller.ScanWaybill(Guid.NewGuid(), file, CancellationToken.None));

        Assert.Equal(AuthErrorCodes.MissingGuestSession, ex.ErrorCode);
    }

    [Fact]
    public async Task ScanWaybill_EmptyFile_ThrowsBadRequestException()
    {
        var (controller, _) = CreateController();
        var file = CreateMockFormFile(0);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            controller.ScanWaybill(Guid.NewGuid(), file, CancellationToken.None));

        Assert.Equal(ImageErrorCodes.InvalidImage, ex.ErrorCode);
        Assert.Equal("Image must not be empty.", ex.Message);
    }

    [Theory]
    [InlineData(FiveMiB + 1)]
    [InlineData(FiveMiB + 1024)]
    [InlineData(10 * 1024 * 1024)]
    public async Task ScanWaybill_ExceedsFiveMiB_ThrowsBadRequestException(long fileLength)
    {
        var (controller, _) = CreateController();
        var file = CreateMockFormFile(fileLength);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            controller.ScanWaybill(Guid.NewGuid(), file, CancellationToken.None));

        Assert.Equal(ImageErrorCodes.InvalidImage, ex.ErrorCode);
        Assert.Equal("Image must not exceed 5 MB.", ex.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(FiveMiB)]
    public async Task ScanWaybill_WithinFiveMiB_CallsServiceAndReturnsOk(long fileLength)
    {
        var (controller, service) = CreateController();
        var file = CreateMockFormFile(fileLength);
        var id = Guid.NewGuid();

        var result = await controller.ScanWaybill(id, file, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.True(service.ScanWaybillCalled);
    }
}
