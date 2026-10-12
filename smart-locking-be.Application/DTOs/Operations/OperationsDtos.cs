using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.DTOs.Operations;

public sealed record EmergencyUnlockRequest(Guid LockerId, Guid CompartmentId, Guid? IncidentId, string Reason);

public sealed record EmergencyUnlockResponse(Guid Id, EmergencyUnlockResult Result, string CompartmentCode, DateTimeOffset RequestedAt);
