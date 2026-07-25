using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Infrastructure;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Kiểm chứng cấu hình DI của tầng Infrastructure.
/// Các test này KHÔNG cần SQL Server chạy: tạo DbContext không mở connection,
/// EF Core chỉ mở connection khi thực sự query hoặc SaveChanges.
/// </summary>
public class DependencyInjectionTests
{
    private const string FakeConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=MiniMart_Test;Trusted_Connection=True;TrustServerCertificate=True";

    private static IConfiguration BuildConfiguration(string? connectionString)
    {
        var values = new Dictionary<string, string?>();

        if (connectionString is not null)
        {
            values["ConnectionStrings:DefaultConnection"] = connectionString;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration(FakeConnectionString));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddInfrastructure_PhaiDangKyDbContext_VoiLifetimeScoped()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration(FakeConnectionString));

        var descriptor = services.Single(d => d.ServiceType == typeof(MiniMartDbContext));

        // Singleton -> vỡ thread-safety + memory leak; Transient -> vỡ Unit of Work.
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void CungMotScope_PhaiTraVeCungMotDbContext()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var first = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
        var second = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Đây là điều kiện để Service và Repository trong cùng 1 HTTP request
        // dùng chung Change Tracker, nhờ đó gom được vào 1 SaveChanges duy nhất.
        Assert.Same(first, second);
    }

    [Fact]
    public void HaiScopeKhacNhau_PhaiTraVeHaiDbContextKhacNhau()
    {
        using var provider = BuildProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var fromA = scopeA.ServiceProvider.GetRequiredService<MiniMartDbContext>();
        var fromB = scopeB.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Hai HTTP request khác nhau không được dùng chung DbContext:
        // DbContext không thread-safe, và Change Tracker sẽ rò dữ liệu giữa user.
        Assert.NotSame(fromA, fromB);
    }

    [Fact]
    public void ThieuConnectionString_PhaiNemLoiNgayLucDangKy()
    {
        var services = new ServiceCollection();

        // Fail fast: sai cấu hình phải chết lúc khởi động, không phải lúc
        // request đầu tiên chạm vào DB.
        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(BuildConfiguration(null)));

        Assert.Contains("DefaultConnection", ex.Message);
    }
}
