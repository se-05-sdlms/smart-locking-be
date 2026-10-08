using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController]
[Route("api/registration-lockers")]
[AllowAnonymous]
public sealed class RegistrationLockersController(IAuthService authService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await authService.GetRegistrationLockersAsync(cancellationToken));
}
