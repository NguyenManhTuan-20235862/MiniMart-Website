using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Application.Interfaces;
using MiniMart.Application.Services;
using MiniMart.Domain.Entities;

namespace MiniMart.Application;

/// <summary>
/// Gom toàn bộ đăng ký DI của tầng Application (business logic) vào một chỗ.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();

        // Phụ thuộc ICartStore - do factory ở Program.cs quyết định là kho Session
        // hay kho DB. CartService không biết và không cần biết.
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();

        // Scoped như mọi service khác: nó dùng IUnitOfWork và Repository, tức dùng
        // chung DbContext của request.
        services.AddScoped<IPaymentService, PaymentService>();

        // Singleton hợp lệ ở đây: PasswordHasher không giữ state và không phụ
        // thuộc DbContext. Nếu nó phụ thuộc thứ gì Scoped thì Singleton sẽ tạo
        // ra captive dependency - object Scoped bị giữ sống vĩnh viễn.
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        return services;
    }
}
