using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Users;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IUserService
{
    Task<PagedResult<UserListItemResponse>> GetUsersAsync(
        GetUsersFilterRequest filter,
        CancellationToken cancellationToken = default);

    Task<UserDetailResponse> GetUserByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CreateUserResponse> CreateUserAsync(
        Guid actorAdminId,
        CreateUserRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<UserDetailResponse> UpdateUserAsync(
        Guid actorAdminId,
        Guid id,
        UpdateUserRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<UserDetailResponse> UpdateUserStatusAsync(
        Guid actorAdminId,
        Guid id,
        UpdateUserStatusRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<OperatorAssignmentResponse> AssignOperatorScopeAsync(
        Guid actorAdminId,
        Guid operatorId,
        AssignOperatorScopeRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task RevokeOperatorScopeAsync(
        Guid actorAdminId,
        Guid operatorId,
        Guid assignmentId,
        string? reason = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}
