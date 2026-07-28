using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniMart.Domain.Entities;

namespace MiniMart.Infrastructure.Data.Configurations;

public class OrderDetailConfiguration : IEntityTypeConfiguration<OrderDetail>
{
    public void Configure(EntityTypeBuilder<OrderDetail> builder)
    {
        builder.HasKey(d => d.Id);

        builder.HasOne(d => d.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(d => d.OrderId)
            // Các dòng không tồn tại độc lập với đơn.
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.Product)
            .WithMany()
            .HasForeignKey(d => d.ProductId)
            // ★ Restrict, NGƯỢC HẲN với CartItem -> Product (Cascade).
            //
            // Cascade ở đây sẽ làm mất dòng đơn hàng khi admin xoá sản phẩm - tức
            // lịch sử bán hàng tự sửa lại chính nó và tổng tiền đơn không còn khớp
            // tổng các dòng. Với bản ghi tài chính, đó là lỗi nghiêm trọng nhất.
            //
            // Hệ quả cố ý: sản phẩm đã từng được đặt thì không xoá được nữa (xem
            // "nợ kỹ thuật" - Admin cần một thông báo tử tế cho trường hợp này).
            // Việc đúng với hàng ngừng bán là ẩn/khoá, không phải xoá.
            .OnDelete(DeleteBehavior.Restrict);

        // Snapshot tên: dài hơn Product.Name một chút không sao, nhưng phải Required
        // - dòng đơn hàng không có tên là dòng không đọc được.
        builder.Property(d => d.ProductName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.UnitPrice)
            .HasPrecision(18, 2);

        // LineTotal là thuộc tính tính toán trong entity nên phải nói rõ đừng map
        // xuống cột. EF Core mặc định coi property có getter là cột và migration sẽ
        // sinh ra một cột LineTotal không bao giờ được ghi.
        builder.Ignore(d => d.LineTotal);

        // Quantity > 0: dòng số lượng 0 thì lẽ ra không được tạo.
        // UnitPrice >= 0: khớp quy ước của Products.Price, và chặn giá âm lọt vào
        // hoá đơn nếu logic snapshot sai.
        //
        // Cả hai là ràng buộc DB chứ không chỉ kiểm tra ở Service, đúng quy ước
        // "validate ở Service để có thông báo tử tế, ràng buộc ở DB để có sự thật".
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_OrderDetails_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_OrderDetails_UnitPrice_NonNegative", "[UnitPrice] >= 0");
        });

        // KHÔNG có unique index (OrderId, ProductId), cố ý khác CartItem.
        //
        // Giỏ hàng cần "một sản phẩm một dòng" để productId dùng được làm khoá.
        // Đơn hàng thì không: nó là bản ghi lịch sử, và nếu về sau có nghiệp vụ cho
        // phép cùng một sản phẩm xuất hiện hai dòng với hai mức giá (khuyến mãi một
        // phần), ràng buộc này sẽ chặn oan.
    }
}
