using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.API.Controllers;
using smart_locking_be.Application.DTOs.Returns;
using smart_locking_be.Application.Interfaces.Services;
using System.Net;

namespace smart_locking_be.Tests.API;

public sealed class ReturnPickupClientAddressTests
{
    [Theory]
    [InlineData("192.0.2.1")]
    [InlineData(null)]
    public async Task ControllerPassesConnectionAddressAndIgnoresUntrustedForwardingHeader(string? remoteIp)
    {
        var service = new CapturingService();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        var controller = new ReturnPickupSessionsController(service) { ControllerContext = new ControllerContext { HttpContext = context } };
        await controller.Create(new ValidateReturnPickupRequest("TEST", "123456"), default);
        Assert.Equal(remoteIp, service.ClientIp);
    }

    private sealed class CapturingService : IReturnPickupSessionService
    {
        public string? ClientIp { get; private set; }
        public Task<ReturnPickupSessionResponse> CreateAsync(ValidateReturnPickupRequest request, CancellationToken cancellationToken = default, string? clientIpAddress = null)
        {
            ClientIp = clientIpAddress;
            return Task.FromResult(new ReturnPickupSessionResponse(Guid.NewGuid(), "token", "TEST", "A1", "", DateTimeOffset.UtcNow));
        }
        public Task<ReturnPickupSessionResponse> GetAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReturnPickupSessionResponse> OpenCompartmentAsync(Guid id, string guestSessionToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task FinalizePickupAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
