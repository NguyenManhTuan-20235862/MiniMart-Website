using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure.Data;
using MiniMart.Infrastructure.Payments;
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
        services.AddScoped<IPaymentRepository, PaymentRepository>();

        // Đăng ký bằng CHÍNH class, không phải qua ICartStore.
        //
        // Lý do: ICartStore có HAI cài đặt và việc chọn cái nào phụ thuộc người
        // dùng đã đăng nhập hay chưa - thứ mà tầng Infrastructure không biết.
        // Quyết định đó nằm ở Composition Root (Program.cs), và factory ở đó cần
        // resolve được đúng class này. Không đăng ký thì factory ném lúc chạy.
        services.AddScoped<DatabaseCartStore>();

        AddVnPay(services, configuration);

        return services;
    }

    /// <summary>
    /// Cấu hình cổng thanh toán VNPay theo Options pattern.
    ///
    /// <para>
    /// <c>Bind</c> chứ không đọc trực tiếp <c>configuration["VnPay:HashSecret"]</c> ở nơi
    /// cần dùng. Ba khác biệt thật, không phải hình thức:
    /// </para>
    /// <list type="number">
    /// <item>Class nhận <c>IOptions&lt;VnPayOptions&gt;</c> chỉ phụ thuộc DỮ LIỆU nó cần,
    /// không phụ thuộc <c>IConfiguration</c> - tức không có quyền đọc connection string
    /// hay bất cứ khoá nào khác. Đúng tinh thần Interface Segregation.</item>
    /// <item>Test dựng thẳng <c>Options.Create(new VnPayOptions { ... })</c>, không cần
    /// giả lập cả một cây cấu hình.</item>
    /// <item>Sai chính tả tên khoá trở thành một lỗi CÓ THỂ PHÁT HIỆN - xem validator.
    /// Đọc bằng indexer thì <c>configuration["VnPay:HasSecret"]</c> trả về null, và
    /// null đó lặng lẽ chảy tới tận lúc ký.</item>
    /// </list>
    /// </summary>
    private static void AddVnPay(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VnPayOptions>()
            .Bind(configuration.GetSection(VnPayOptions.SectionName))

            // ValidateOnStart: chạy validator lúc ứng dụng khởi động. Không có nó thì
            // Options được tạo LƯỜI - lỗi cấu hình chỉ lộ ra ở request đầu tiên chạm
            // tới thanh toán, tức đúng lúc có một khách hàng thật đang chờ.
            .ValidateOnStart();

        // Validator đăng ký riêng qua DI (thay vì .Validate(lambda)) để nó tự nhận được
        // dependency về sau nếu cần, và để có thể unit test thẳng class này.
        services.AddSingleton<IValidateOptions<VnPayOptions>, VnPayOptionsValidator>();

        // TimeProvider.System: đồng hồ thật, tiêm qua DI để test thay được bằng đồng
        // hồ đứng yên. Là abstraction CÓ SẴN của .NET 8+, nên không tự viết IClock -
        // interface tự chế cho một khái niệm framework đã chuẩn hoá chỉ tạo thêm một
        // thứ phải học cho người đọc code.
        //
        // TryAddSingleton chứ không AddSingleton: nếu về sau có nơi khác (hoặc test)
        // đăng ký TimeProvider trước thì giữ nguyên đăng ký đó, không chồng thêm.
        services.TryAddSingleton(TimeProvider.System);

        // Singleton: không giữ state thay đổi, chỉ đọc VnPayOptions và băm. Scoped ở
        // đây là tạo mới một object không có lý do gì phải mới cho mỗi request.
        services.AddSingleton<IVnPayService, VnPayService>();
    }
}
