using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Interfaces;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

/// <summary>
/// Class mẫu cài đặt IService thuộc tầng Infrastructure.
/// Tiêm (inject) trực tiếp ApplicationDbContext để thao tác trực tiếp với cơ sở dữ liệu PostgreSQL.
/// </summary>
public class Service : IService
{
    private readonly ApplicationDbContext _dbContext;

    public Service(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> GetWelcomeMessageAsync(CancellationToken cancellationToken = default)
    {
        // Ví dụ thao tác trực tiếp với DbContext mà không qua lớp Repository trung gian
        int userCount = await _dbContext.Users.CountAsync(cancellationToken);

        return $"Chào mừng bạn đến với hệ thống Smart Locking (SDLMS)! Hệ thống hiện có {userCount} người dùng trong cơ sở dữ liệu.";
    }
}
