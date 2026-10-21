using smart_locking_be.Domain.Entities;

namespace smart_locking_be.Application.Interfaces.Services;

/// <summary>
/// Cổng truy cập dữ liệu cho nghiệp vụ đặt lại ngăn. Implementation giữ entity được theo dõi
/// trong cùng một đơn vị công việc; Application không phụ thuộc EF, SQL hoặc DbContext.
/// </summary>
public interface ICompartmentResetStore
{
    // Chỉ công nhận phân công đã có hiệu lực và chưa bị thu hồi tại thời điểm thao tác.
    Task<bool> IsOperatorAssignedAsync(Guid actorId, Guid lockerId, DateTimeOffset now, CancellationToken ct);
    Task<LockerCompartment?> FindSlotAsync(Guid? lockerId, Guid slotId, CancellationToken ct);
    Task<bool> HasOtherGoodsAsync(Guid slotId, Guid subjectId, DateTimeOffset now, CancellationToken ct);
    Task<AuditLog?> FindPendingAsync(Guid slotId, CancellationToken ct);
    Task<AuditLog?> FindRequestAsync(Guid commandId, CancellationToken ct);
    Task<Guid?> FindSuggestedSubjectAsync(Guid slotId, CancellationToken ct);
    Task<bool> SubjectBelongsToSlotAsync(Guid subjectId, Guid slotId, CancellationToken ct);
    Task<bool> IsAppliedAsync(Guid commandId, CancellationToken ct);
    Task<List<Guid>> GetAcknowledgedEventIdsAsync(string device, int channel, Guid subjectId, CancellationToken ct);
    Task<string?> GetLastFailureAsync(Guid commandId, CancellationToken ct);
    Task<ReturnRequest?> FindReturnAsync(Guid subjectId, CancellationToken ct);
    Task<DeliveryRequest?> FindDeliveryAsync(Guid subjectId, CancellationToken ct);
    Task<Parcel?> FindTransferParcelAsync(Guid parcelId, CancellationToken ct);
    Task<List<AuditLog>> GetPendingRequestsAsync(CancellationToken ct);
    Task<List<AuditLog>> GetAppliedRequestsAsync(Guid slotId, CancellationToken ct);
    Task<List<Parcel>> GetStoredParcelsAsync(Guid deliveryId, CancellationToken ct);
    Task<List<CompartmentReservation>> GetReservationsAsync(Guid slotId, Guid subjectId, CancellationToken ct);
    Task<List<OfflineLockerPin>> GetPinsAsync(Guid subjectId, CancellationToken ct);
    void Add(AuditLog audit);
    void Add(ParcelStatusHistory history);
    void Add(LockerEvent lockerEvent);
    void Add(Notification notification);
    Task SaveChangesAsync(CancellationToken ct);
    Task<ICompartmentResetTransaction?> BeginTransactionAsync(CancellationToken ct);
}

