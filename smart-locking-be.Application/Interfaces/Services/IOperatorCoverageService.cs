using smart_locking_be.Application.DTOs.Users;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IOperatorCoverageService
{
    Task<LockerLocationResponse> GetLocationAsync(Guid lockerId, CancellationToken ct);
    Task<LockerLocationResponse> ConfirmLocationAsync(Guid adminId, Guid lockerId, ConfirmLockerLocationRequest request, CancellationToken ct);
    Task<IReadOnlyCollection<OperatorAbsenceResponse>> ListAsync(Guid actorId, string role, CancellationToken ct);
    Task<OperatorAbsenceResponse> CreateAsync(Guid actorId, string role, CreateOperatorAbsenceRequest request, CancellationToken ct);
    Task<OperatorAbsenceResponse> SuggestAsync(Guid adminId, Guid absenceId, CancellationToken ct);
    Task<OperatorAbsenceResponse> ApproveAsync(Guid adminId, Guid proposalId, ReviewOperatorReplacementRequest request, CancellationToken ct);
    Task<OperatorAbsenceResponse> CancelAsync(Guid actorId, string role, Guid absenceId, string reason, CancellationToken ct);
    Task ReconcileAsync(CancellationToken ct);
}
