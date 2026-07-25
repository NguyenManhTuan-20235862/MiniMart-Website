namespace MiniMart.Domain.Entities;

/// <summary>
/// Tài khoản đăng nhập. POCO thuần - không có attribute nào của EF Core,
/// cấu hình lưu trữ nằm ở UserConfiguration bên Infrastructure.
/// </summary>
public class User
{
    public int Id { get; set; }

    // "= null!" là lời hứa với compiler: EF Core sẽ gán giá trị khi đọc từ DB,
    // nên đừng cảnh báo null dù Nullable đang bật.
    public string Username { get; set; } = null!;

    // Chỉ lưu HASH, không bao giờ lưu mật khẩu gốc.
    public string PasswordHash { get; set; } = null!;

    public UserRole Role { get; set; } = UserRole.Customer;
}
