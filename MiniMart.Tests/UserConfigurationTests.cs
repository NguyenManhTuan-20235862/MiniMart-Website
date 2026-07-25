using Microsoft.EntityFrameworkCore;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Kiểm chứng UserConfiguration bằng metadata model của EF Core.
/// Không cần SQL Server: dựng model là thao tác thuần trong bộ nhớ.
/// Các test này bắt lỗi cấu hình TRƯỚC khi nó kịp đi vào một migration sai.
/// </summary>
public class UserConfigurationTests
{
    private static MiniMartDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MiniMartDbContext>()
            .UseSqlServer("Server=khong-ket-noi;Database=MiniMart_Test")
            .Options;

        return new MiniMartDbContext(options);
    }

    [Fact]
    public void User_PhaiAnhXaToiBang_Users()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(User))!;

        Assert.Equal("Users", entityType.GetTableName());
    }

    [Fact]
    public void Username_PhaiRequired_VaGioiHan50KyTu()
    {
        using var context = CreateContext();

        var property = context.Model
            .FindEntityType(typeof(User))!
            .FindProperty(nameof(User.Username))!;

        Assert.False(property.IsNullable);
        Assert.Equal(50, property.GetMaxLength());
    }

    [Fact]
    public void Username_PhaiCoUniqueIndex()
    {
        using var context = CreateContext();

        var index = context.Model
            .FindEntityType(typeof(User))!
            .GetIndexes()
            .Single(i => i.Properties.Any(p => p.Name == nameof(User.Username)));

        // Mất unique index nghĩa là 2 tài khoản trùng username lọt được vào DB.
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void PasswordHash_PhaiRequired_VaGioiHan255KyTu()
    {
        using var context = CreateContext();

        var property = context.Model
            .FindEntityType(typeof(User))!
            .FindProperty(nameof(User.PasswordHash))!;

        Assert.False(property.IsNullable);
        Assert.Equal(255, property.GetMaxLength());
    }

    [Fact]
    public void Role_PhaiLuuXuongDbDangChuoi_KhongPhaiSo()
    {
        using var context = CreateContext();

        var property = context.Model
            .FindEntityType(typeof(User))!
            .FindProperty(nameof(User.Role))!;

        // Bỏ HasConversion<string>() thì enum lặng lẽ quay về int, và migration
        // kế tiếp sẽ sinh AlterColumn đổi kiểu cột - test này chặn từ đầu.
        Assert.Equal(typeof(string), property.GetProviderClrType());
        Assert.StartsWith("nvarchar", property.GetColumnType());
    }
}
