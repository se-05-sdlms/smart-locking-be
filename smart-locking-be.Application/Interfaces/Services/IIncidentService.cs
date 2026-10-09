using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Incidents;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IIncidentService
{
    Task<GuestIncidentResponse> CreateGuestIncidentAsync(
        CreateGuestIncidentRequest request,
        CancellationToken cancellationToken = default);

    Task<IncidentDetailResponse> CreateResidentIncidentAsync(
        Guid residentUserId,
        CreateIncidentRequest request,
        CancellationToken cancellationToken = default);

    Task<PagedResult<IncidentListItemResponse>> GetResidentIncidentsAsync(
        Guid residentUserId,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20);

    Task<PagedResult<IncidentListItemResponse>> GetOperationalIncidentsAsync(
        Guid userId,
        string role,
        IncidentStatus? status,
        CancellationToken cancellationToken = default,
        int pageNumber = 1,
        int pageSize = 20);

    Task<IncidentDetailResponse> GetIncidentAsync(
        Guid userId,
        string role,
        Guid incidentId,
        CancellationToken cancellationToken = default);

    Task<IncidentDetailResponse> AddActionAsync(
        Guid userId,
        string role,
        Guid incidentId,
        AddIncidentActionRequest request,
        CancellationToken cancellationToken = default);

    Task<IncidentDetailResponse> UpdateStatusAsync(
        Guid userId,
        string role,
        Guid incidentId,
        UpdateIncidentStatusRequest request,
        CancellationToken cancellationToken = default);
}
