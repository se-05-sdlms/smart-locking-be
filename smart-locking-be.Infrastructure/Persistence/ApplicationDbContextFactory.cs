using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace smart_locking_be.Infrastructure.Persistence;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        string? connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("EF_DEFAULT_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Chưa cấu hình Chuỗi kết nối Database ('ConnectionStrings__DefaultConnection' hoặc 'EF_DEFAULT_CONNECTION') cho Design-time DbContext Factory. " +
                "Vui lòng thiết lập biến môi trường trước khi chạy CLI EF Core.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
