using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Kiểm chứng cấu hình Product/Category qua metadata model - không cần DB.
/// </summary>
public class ProductConfigurationTests
{
    private static MiniMartDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MiniMartDbContext>()
            .UseSqlServer("Server=khong-ket-noi;Database=MiniMart_Test")
            .Options;

        return new MiniMartDbContext(options);
    }

    [Fact]
    public void Price_phai_la_decimal_18_2()
    {
        using var context = CreateContext();

        var price = context.Model
            .FindEntityType(typeof(Product))!
            .FindProperty(nameof(Product.Price))!;

        // Không khai báo precision thì phó mặc mặc định của provider - với
        // tiền tệ đó là rủi ro làm tròn không kiểm soát.
        Assert.Equal(18, price.GetPrecision());
        Assert.Equal(2, price.GetScale());
    }

    [Fact]
    public void RowVersion_phai_la_concurrency_token_do_DB_sinh()
    {
        using var context = CreateContext();

        var rowVersion = context.Model
            .FindEntityType(typeof(Product))!
            .FindProperty(nameof(Product.RowVersion))!;

        // Mất IsConcurrencyToken thì EF Core quay về "last-in-wins": câu UPDATE
        // không còn kẹp RowVersion vào WHERE, và mọi xung đột ghi đè im lặng.
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
    }

    [Fact]
    public void Xoa_Category_con_san_pham_phai_bi_chan_khong_duoc_cascade()
    {
        using var context = CreateContext();

        var foreignKey = context.Model
            .FindEntityType(typeof(Product))!
            .GetForeignKeys()
            .Single();

        // Cascade ở đây nghĩa là xoá 1 danh mục sẽ xoá sạch sản phẩm bên trong.
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal(nameof(Product.CategoryId), foreignKey.Properties.Single().Name);
    }

    [Fact]
    public void Quan_he_1_N_phai_noi_dung_hai_dau()
    {
        using var context = CreateContext();

        var foreignKey = context.Model
            .FindEntityType(typeof(Product))!
            .GetForeignKeys()
            .Single();

        Assert.Equal(typeof(Category), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(Product.Category), foreignKey.DependentToPrincipal?.Name);
        Assert.Equal(nameof(Category.Products), foreignKey.PrincipalToDependent?.Name);
    }

    [Fact]
    public void Phai_co_check_constraint_chan_Stock_va_Price_am()
    {
        using var context = CreateContext();

        // Check constraint chỉ sinh ra DDL, không tham gia truy vấn, nên EF Core
        // lược nó khỏi model runtime đã tối ưu. Phải hỏi model design-time.
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        var checkConstraints = designTimeModel
            .FindEntityType(typeof(Product))!
            .GetCheckConstraints()
            .Select(c => c.Name)
            .ToList();

        // Chốt chặn cuối cho nghiệp vụ trừ tồn kho ở phase Concurrency.
        Assert.Contains("CK_Products_Stock_NonNegative", checkConstraints);
        Assert.Contains("CK_Products_Price_NonNegative", checkConstraints);
    }

    [Fact]
    public void Category_Name_phai_unique()
    {
        using var context = CreateContext();

        var index = context.Model
            .FindEntityType(typeof(Category))!
            .GetIndexes()
            .Single(i => i.Properties.Any(p => p.Name == nameof(Category.Name)));

        Assert.True(index.IsUnique);
    }
}
