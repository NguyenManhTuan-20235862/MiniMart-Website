using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Infrastructure;

/// <summary>
/// Gom toàn bộ đăng ký DI của tầng Infrastructure vào một chỗ, để Program.cs
/// không cần biết tên class implementation cụ thể nào.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Fail fast: thiếu connection string thì chết ngay lúc khởi động, kèm thông
        // báo rõ ràng — thay vì chạy được rồi mới lỗi khó hiểu ở request đầu tiên.
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Thiếu 'ConnectionStrings:DefaultConnection'. Kiểm tra appsettings.json hoặc User Secrets.");

        // AddDbContext đăng ký với lifetime Scoped (mặc định): mỗi HTTP request
        // dùng chung đúng 1 DbContext -> Service và Repository cùng 1 transaction.
        services.AddDbContext<MiniMartDbContext>(options =>
            options.UseSqlServer(connectionString));

        // ─────────────────────────────────────────────────────────────
        // Phase 2: đăng ký Repository tại đây
        // services.AddScoped<IProductRepository, ProductRepository>();
        // services.AddScoped<IOrderRepository, OrderRepository>();
        // ─────────────────────────────────────────────────────────────

        return services;
    }
}
