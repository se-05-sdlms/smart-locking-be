using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using System.Security.Claims;

namespace smart_locking_be.API.Hubs;

[Authorize]
public sealed class OperationsHub(ApplicationDbContext dbContext) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        await base.OnConnectedAsync();
    }

    public async Task SubscribeLocker(Guid lockerId)
    {
        if (!Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
            throw new HubException("Unauthorized.");
        string? role = Context.User?.FindFirstValue(ClaimTypes.Role);
        bool allowed = role == nameof(UserRole.Administrator) ||
            role == nameof(UserRole.LockerOperator) && await dbContext.OperatorAssignments.AnyAsync(item =>
                item.OperatorUserId == userId && item.LockerId == lockerId && item.RevokedAt == null,
                Context.ConnectionAborted);
        if (!allowed) throw new HubException("Locker is outside the assigned scope.");
        await Groups.AddToGroupAsync(Context.ConnectionId, LockerGroup(lockerId));
    }

    public static string UserGroup(Guid userId) => $"user:{userId:N}";
    public static string LockerGroup(Guid lockerId) => $"locker:{lockerId:N}";
}
