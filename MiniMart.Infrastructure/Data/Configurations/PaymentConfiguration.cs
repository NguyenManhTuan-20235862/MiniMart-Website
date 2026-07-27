using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniMart.Domain.Entities;

namespace MiniMart.Infrastructure.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);

        // Một-một: một đơn tối đa MỘT bản ghi thanh toán.
        //
        // Restrict như OrderDetail -> Order? KHÔNG - ở đây là Cascade theo chiều
        // Order -> Payment, vì bản ghi thanh toán không tồn tại độc lập với đơn của
        // nó. Nhưng điều đó gần như không bao giờ xảy ra: Order -> User đã là Restrict
        // nên đơn không xoá được khi tài khoản còn.
        builder.HasOne(p => p.Order)
            .WithOne(o => o.Payment)
            .HasForeignKey<Payment>(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // ★ UNIQUE trên OrderId - đây là thứ làm cho IPN idempotent THẬT SỰ.
        //
        // Lệnh kiểm "đơn đã Paid chưa" ở PaymentService là đường đẹp, có khe TOCTOU:
        // hai IPN song song đều đọc thấy Pending. Index này là bảo đảm cuối cùng -
        // đúng một INSERT thành công, cái còn lại nhận lỗi 2601/2627 và được UnitOfWork
        // dịch thành DuplicateKeyException.
        //
        // Quan hệ một-một ở trên đã tự tạo unique index, nhưng khai báo tường minh để
        // nó có TÊN nói rõ ý định và không biến mất nếu quan hệ được sửa thành một-nhiều.
        builder.HasIndex(p => p.OrderId)
            .IsUnique()
            .HasDatabaseName("UX_Payments_OrderId");

        // Enum lưu thành CHUỖI, không phải int.
        //
        // Đánh đổi đã cân nhắc: int tốn ít chỗ hơn và so sánh nhanh hơn, nhưng đọc
        // bảng bằng sqlcmd sẽ thấy "0" và "1" - phải mở code mới biết nghĩa. Nặng hơn
        // thế: chèn một giá trị mới vào GIỮA enum làm mọi dòng cũ đổi nghĩa trong im
        // lặng, không migration nào báo. Với cột trạng thái tài chính thì đọc được và
        // ổn định quan trọng hơn vài byte.
        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Amount)
            .HasPrecision(18, 2);

        builder.Property(p => p.TransactionNo)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.BankCode)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(p => p.ResponseCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.ToTable(t =>
            t.HasCheckConstraint("CK_Payments_Amount_NonNegative", "[Amount] >= 0"));
    }
}
