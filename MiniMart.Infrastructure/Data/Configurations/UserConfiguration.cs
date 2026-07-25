using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniMart.Domain.Entities;

namespace MiniMart.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(50);

        // Unique index: chặn trùng username ở tầng DB. Kiểm tra bằng code trong
        // Service vẫn có thể lọt khi 2 request đăng ký cùng lúc - ràng buộc ở DB
        // là chốt chặn cuối cùng.
        builder.HasIndex(u => u.Username)
            .IsUnique();

        // 255 đủ chỗ cho hash của BCrypt (60) hay ASP.NET Identity (~84),
        // dư ra phòng khi đổi thuật toán sau này.
        builder.Property(u => u.PasswordHash)
            .IsRequired()
            .HasMaxLength(255);

        // Lưu enum thành chuỗi "Customer"/"Admin" thay vì 0/1 cho dễ đọc khi
        // query tay. Đánh đổi: thêm role mới phải giữ đúng chính tả.
        builder.Property(u => u.Role)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion<string>();
    }
}
