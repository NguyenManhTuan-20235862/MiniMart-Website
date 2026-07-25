using Microsoft.Extensions.DependencyInjection;

namespace MiniMart.Application;

/// <summary>
/// Gom toàn bộ đăng ký DI của tầng Application (business logic) vào một chỗ.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // ─────────────────────────────────────────────────────────────
        // Phase 2: đăng ký Service tại đây
        // services.AddScoped<IProductService, ProductService>();
        // services.AddScoped<IOrderService, OrderService>();
        // ─────────────────────────────────────────────────────────────

        return services;
    }
}
