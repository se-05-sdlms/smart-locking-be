namespace smart_locking_be.Application.DTOs.Operations;

public sealed record OperatorTaskResponse(string Kind, Guid Id, Guid LockerId, string LockerCode, string Title,
    string Priority, string Status, DateTimeOffset? DueAt, DateTimeOffset CreatedAt, string SourcePath);
public sealed record OperatorTaskPage(IReadOnlyList<OperatorTaskResponse> Items, int Offset, int Limit, bool HasMore);
public sealed record CreateTaskHandoverRequest(string WorkDone, string Blockers, string NextSteps, Guid? IntendedReceiverUserId = null);
public sealed record AcknowledgeTaskHandoverRequest(string Notes);
public sealed record TaskAcknowledgementResponse(Guid Id, Guid ReceiverUserId, string ReceiverName, string Notes, DateTimeOffset CreatedAt);
public sealed record TaskHandoverResponse(Guid Id, string Kind, Guid TaskId, Guid LockerId, Guid AuthorUserId, string AuthorName,
    Guid? IntendedReceiverUserId, string WorkDone, string Blockers, string NextSteps, DateTimeOffset CreatedAt,
    IReadOnlyList<TaskAcknowledgementResponse> Acknowledgements);
