using smart_locking_be.Application.DTOs.Operations;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IOperatorTaskService
{
    Task<OperatorTaskPage> ListAsync(Guid userId, string role, int offset, int limit, Guid? lockerId, CancellationToken ct);
    Task<IReadOnlyList<TaskHandoverResponse>> HistoryAsync(Guid userId, string role, string kind, Guid taskId, CancellationToken ct);
    Task<TaskHandoverResponse> CreateHandoverAsync(Guid userId, string role, string kind, Guid taskId, CreateTaskHandoverRequest request, CancellationToken ct);
    Task<TaskAcknowledgementResponse> AcknowledgeAsync(Guid userId, string role, Guid handoverId, AcknowledgeTaskHandoverRequest request, CancellationToken ct);
}
