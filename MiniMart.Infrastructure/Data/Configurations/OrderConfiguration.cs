using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniMart.Domain.Entities;

namespace MiniMart.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);

        builder.HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            // Restrict, KHÔNG Cascade như Cart -> User.
            //
            // Giỏ hàng là dữ liệu tạm nên xoá tài khoản thì xoá luôn là đúng. Đơn
            // hàng là bản ghi TÀI CHÍNH: nó phải sống lâu hơn tài khoản đặt nó, vì
            // shop cần đối chiếu doanh thu và khách cần tra lại đơn cũ.
            //
            // Hệ quả cố ý: không xoá được User đã từng đặt hàng. Đúng - việc cần làm
            // với tài khoản như vậy là vô hiệu hoá, không phải xoá.
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(o => o.TotalAmount)
            // decimal(18,2) như mọi cột tiền khác trong dự án. Dùng double/float ở
            // cột tiền là chấp nhận sai số nhị phân trên số tiền thật.
            .HasPrecision(18, 2);

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        // Enum lưu thành CHUỖI - cùng lý do với Payment.Status, xem PaymentConfiguration.
        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Ba cột giao hàng: IsRequired + HasMaxLength khớp ĐÚNG với Data Annotation
        // trên CheckoutViewModel. Đây là quy tắc "validate ở Service/ViewModel để có
        // thông báo tử tế, ràng buộc ở DB để có sự thật" - lệch số giữa hai nơi thì
        // request hợp lệ ở tầng Web lại nổ khi lưu.
        //
        // Không có HasMaxLength thì EF Core sinh nvarchar(max): SQL Server không đánh
        // index được trên cột đó và mỗi dòng tốn thêm chi phí lưu trữ ngoài trang dữ
        // liệu, đổi lấy một sự "phòng xa" mà không ai cần.
        builder.Property(o => o.RecipientName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.RecipientPhone)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(o => o.ShippingAddress)
            .IsRequired()
            .HasMaxLength(300);

        // Danh sách đơn của một người, sắp theo thời gian - truy vấn chắc chắn sẽ có
        // ở trang "Đơn hàng của tôi". CreatedAt giảm dần nằm ngay trong index nên
        // không phải sort lại.
        builder.HasIndex(o => new { o.UserId, o.CreatedAt })
            .IsDescending(false, true);

        builder.ToTable(t =>
            t.HasCheckConstraint("CK_Orders_TotalAmount_NonNegative", "[TotalAmount] >= 0"));
    }
}
