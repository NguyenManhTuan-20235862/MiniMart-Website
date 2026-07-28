using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniMart.Domain.Entities;

namespace MiniMart.Infrastructure.Data.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.HasKey(c => c.Id);

        builder.HasOne(c => c.User)
            .WithOne()
            .HasForeignKey<Cart>(c => c.UserId)
            // Cascade: xoá tài khoản thì giỏ hàng của họ không còn ý nghĩa gì.
            // Khác hẳn Category -> Product (Restrict) vì ở đó sản phẩm là dữ
            // liệu nghiệp vụ có giá trị độc lập, còn giỏ hàng thì không.
            .OnDelete(DeleteBehavior.Cascade);

        // "Mỗi người dùng một giỏ" phải là SỰ THẬT ở DB, không chỉ là quy ước
        // trong Service. Không có index này thì một lỗi race lúc tạo giỏ sẽ sinh
        // ra hai giỏ cho cùng một người, và người dùng thấy hàng lúc có lúc không
        // tuỳ giỏ nào được đọc trước.
        builder.HasIndex(c => c.UserId)
            .IsUnique();
    }
}
