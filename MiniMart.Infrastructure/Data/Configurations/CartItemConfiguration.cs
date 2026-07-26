using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniMart.Domain.Entities;

namespace MiniMart.Infrastructure.Data.Configurations;

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.HasKey(i => i.Id);

        builder.HasOne(i => i.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.CartId)
            // Xoá giỏ thì các dòng trong giỏ đi theo - chúng không tồn tại độc lập.
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            // Cascade chứ KHÔNG Restrict, cố ý khác quy tắc của Category -> Product.
            //
            // Restrict ở đây nghĩa là: chỉ cần MỘT khách bất kỳ còn sản phẩm đó
            // trong giỏ là admin không xoá được sản phẩm. Admin không nhìn thấy
            // giỏ của ai nên sẽ không hiểu vì sao bị chặn.
            //
            // Cascade thì sản phẩm bị xoá sẽ tự biến khỏi mọi giỏ - đúng thứ
            // người dùng mong đợi với một món hàng ngừng bán.
            .OnDelete(DeleteBehavior.Cascade);

        // ★ Ràng buộc trung tâm của thiết kế này.
        //
        // Nó bảo đảm mỗi sản phẩm chỉ có một dòng trong một giỏ, và chính nhờ vậy
        // mà toàn bộ API giỏ hàng dùng được productId làm khoá thay vì cartItemId
        // - tức là không có Id nào của người khác để mà đoán (chống IDOR).
        //
        // Service vẫn phải tự kiểm tra để cộng dồn số lượng thay vì chèn dòng mới,
        // nhưng đó là để có thông báo tử tế; index này mới là sự thật.
        builder.HasIndex(i => new { i.CartId, i.ProductId })
            .IsUnique();

        // Số lượng 0 thì dòng phải bị xoá hẳn, không phải nằm lại với số 0.
        // Số âm thì vô nghĩa. Chốt ở DB vì kiểm tra ở Service luôn có khe TOCTOU.
        builder.ToTable(t =>
            t.HasCheckConstraint("CK_CartItems_Quantity_Positive", "[Quantity] > 0"));
    }
}
