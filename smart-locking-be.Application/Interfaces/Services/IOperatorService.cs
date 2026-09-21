using smart_locking_be.Application.DTOs.Common;
using smart_locking_be.Application.DTOs.Operator;
using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IOperatorService
{
    Task<OperatorDashboardResponse> GetDashboardAsync(Guid operatorUserId, CancellationToken cancellationToken = default);

    Task<PagedResult<OperatorIncidentResponse>> GetIncidentsAsync(
        Guid operatorUserId, Guid? lockerId = null, IncidentStatus? status = null,
        int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    Task<PagedResult<OverdueParcelResponse>> GetOverdueParcelsAsync(
        Guid operatorUserId, Guid? lockerId = null, string? search = null,
        bool? threeDaysOrMore = null, int pageNumber = 1, int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<OverdueParcelResponse> GetOverdueParcelAsync(
        Guid operatorUserId, Guid parcelId, CancellationToken cancellationToken = default);
}
