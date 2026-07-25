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

        // Singleton hợp lệ ở đây: PasswordHasher không giữ state và không phụ
        // thuộc DbContext. Nếu nó phụ thuộc thứ gì Scoped thì Singleton sẽ tạo
        // ra captive dependency - object Scoped bị giữ sống vĩnh viễn.
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        return services;
    }
}
