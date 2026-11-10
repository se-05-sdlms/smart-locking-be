using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Lockers;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/lockers/{id:guid}/qr-context")]
[Authorize(Roles = "Administrator,LockerOperator")]
public sealed class LockerQrContextController(ILockerService lockerService, IConfiguration configuration) : ControllerBase
{
    /// <summary>Returns public QR content; the web client renders and downloads the image.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var (userId, role) = User.GetRequiredIdentity();
        // Reuse the same scope as locker management, including effective temporary coverage in #195.
        LockerDetailResponse locker = await lockerService.GetLockerByIdAsync(userId, role, id, cancellationToken);
        string? configuredUrl = configuration["ShipperWeb:BaseUrl"]?.Trim();
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out Uri? baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback)) ||
            !string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new ConflictException("LOCKER_QR_NOT_CONFIGURED", "A public Shipper web URL must be configured for this environment.");

        UriBuilder target = new(baseUri) { Query = "locker=" + Uri.EscapeDataString(locker.Code) };
        return Ok(new LockerQrContextResponse(locker.Id, locker.Code, target.Uri.AbsoluteUri, locker.OperationalStatus));
    }
}
