using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniMart.Domain.Entities;

namespace MiniMart.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        // 18 chữ số tổng, 2 chữ số thập phân. Không khai báo thì phụ thuộc
        // mặc định của provider - với tiền tệ thì không nên phó mặc.
        builder.Property(p => p.Price)
            .HasPrecision(18, 2);

        // IsRowVersion() = IsConcurrencyToken() + ValueGeneratedOnAddOrUpdate().
        // SQL Server sinh kiểu cột "rowversion", tự tăng mỗi lần UPDATE.
        builder.Property(p => p.RowVersion)
            .IsRowVersion();

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            // Restrict: không cho xoá Category còn sản phẩm. Cascade (mặc định)
            // sẽ âm thầm xoá sạch sản phẩm theo danh mục.
            .OnDelete(DeleteBehavior.Restrict);

        // Lọc theo danh mục là truy vấn phổ biến nhất của trang bán hàng.
        builder.HasIndex(p => p.CategoryId);

        // Chốt chặn ở tầng DB: đúng bằng ràng buộc mà phase Concurrency sắp
        // tới phải bảo vệ. Logic ứng dụng có bug thì DB vẫn từ chối.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Products_Stock_NonNegative", "[Stock] >= 0");
            t.HasCheckConstraint("CK_Products_Price_NonNegative", "[Price] >= 0");
        });
    }
}
