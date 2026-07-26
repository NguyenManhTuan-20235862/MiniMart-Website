using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure.Data;
using MiniMart.Infrastructure.Repositories;

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

        // Scoped, đồng bộ với DbContext: cùng 1 request thì Service và
        // Repository dùng chung Change Tracker.
        // Scoped bắt buộc: UnitOfWork phải dùng CHUNG DbContext với mọi
        // Repository trong cùng request, nếu không SaveChanges sẽ không thấy
        // các thay đổi mà Repository đã đánh dấu.
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();

        // Đăng ký bằng CHÍNH class, không phải qua ICartStore.
        //
        // Lý do: ICartStore có HAI cài đặt và việc chọn cái nào phụ thuộc người
        // dùng đã đăng nhập hay chưa - thứ mà tầng Infrastructure không biết.
        // Quyết định đó nằm ở Composition Root (Program.cs), và factory ở đó cần
        // resolve được đúng class này. Không đăng ký thì factory ném lúc chạy.
        services.AddScoped<DatabaseCartStore>();

        return services;
    }
}
