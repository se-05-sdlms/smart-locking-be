using smart_locking_be.Application.DTOs.Admin;
using smart_locking_be.Application.DTOs.Common;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IAdminUserService
{
    Task<PagedResult<AdminResidentListItemResponse>> GetResidentsAsync(
        GetUsersFilterRequest filter,
        CancellationToken cancellationToken = default);

    Task<AdminResidentDetailResponse> GetResidentByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task UpdateResidentStatusAsync(
        Guid adminUserId,
        Guid userId,
        UpdateUserStatusRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AdminOperatorListItemResponse>> GetOperatorsAsync(
        GetUsersFilterRequest filter,
        CancellationToken cancellationToken = default);

    Task<AdminOperatorDetailResponse> GetOperatorByIdAsync(
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
