namespace smart_locking_be.Application.DTOs.Lockers;

// Chỉ trạng thái vận hành/cảm biến công khai; không trả danh tính, mã PIN hoặc ID giao dịch.
public sealed record PublicLockerStatusResponse(string Code, string DeviceIdentifier, string ConnectionStatus,
    DateTimeOffset? LastSeenAt, DateTimeOffset ReadAt, IReadOnlyCollection<PublicCompartmentStatusResponse> Compartments);

public sealed record PublicCompartmentStatusResponse(int Channel, string DoorStatus, DateTimeOffset? ObservedAt, string Usage);
