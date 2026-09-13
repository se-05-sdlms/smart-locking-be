using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Domain.Entities;

/// <summary>
/// Đại diện cho tòa nhà hoặc văn phòng được quản lý trong hệ thống SDLMS.
/// </summary>
public sealed class Building
{
    /// <summary>
    /// Mã định danh duy nhất của tòa nhà.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã nghiệp vụ duy nhất của tòa nhà.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Tên tòa nhà hiển thị cho người dùng và quản trị viên.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Địa chỉ đầy đủ của tòa nhà.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái kích hoạt của tòa nhà trong hệ thống.
    /// </summary>
    public BuildingStatus Status { get; set; }

    /// <summary>
    /// Thời điểm bản ghi tòa nhà được tạo.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm thông tin tòa nhà được cập nhật gần nhất.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Danh sách các cụm locker thuộc tòa nhà.
    /// </summary>
    public ICollection<LockerCluster> LockerClusters { get; set; } = new List<LockerCluster>();

    /// <summary>
    /// Danh sách phân công Nhân viên vận hành quản lý tòa nhà.
    /// </summary>
    public ICollection<OperatorAssignment> OperatorAssignments { get; set; } = new List<OperatorAssignment>();
}
