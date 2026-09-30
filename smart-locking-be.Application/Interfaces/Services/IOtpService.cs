using smart_locking_be.Domain.Enums;

namespace smart_locking_be.Application.Interfaces.Services;

public interface IOtpService
{
    Task IssueAsync(
        string phoneNumber,
        OtpPurpose purpose,
        Guid? userId,
        CancellationToken cancellationToken);

    Task VerifyAndConsumeAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        Guid? userId,
        CancellationToken cancellationToken);
}
