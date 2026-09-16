using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Users;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IUserService
{
    Task<PagedResult<ResidentListItemResponse>> GetResidentsAsync(
        GetUsersFilterRequest filter,
        CancellationToken cancellationToken = default);

    Task<ResidentDetailResponse> GetResidentByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task UpdateResidentStatusAsync(
        Guid adminUserId,
        Guid userId,
        UpdateUserStatusRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<PagedResult<OperatorListItemResponse>> GetOperatorsAsync(
        GetUsersFilterRequest filter,
        CancellationToken cancellationToken = default);

    Task<OperatorDetailResponse> GetOperatorByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<CreateOperatorResponse> CreateOperatorAsync(
        Guid adminUserId,
        CreateOperatorRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task UpdateOperatorStatusAsync(
        Guid adminUserId,
        Guid userId,
        UpdateUserStatusRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<OperatorAssignmentResponse> AssignOperatorScopeAsync(
        Guid adminUserId,
        Guid operatorId,
        AssignOperatorScopeRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task RevokeOperatorScopeAsync(
        Guid adminUserId,
        Guid operatorId,
        Guid assignmentId,
        string? reason = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}
