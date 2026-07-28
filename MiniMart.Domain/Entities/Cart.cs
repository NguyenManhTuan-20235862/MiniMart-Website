namespace MiniMart.Domain.Entities;

/// <summary>
/// Giỏ hàng của một người dùng ĐÃ ĐĂNG NHẬP. Khách vãng lai không có bản ghi
/// nào ở đây - giỏ của họ nằm trong Session, xem SessionCartStore.
///
/// <para>
/// Tách Cart ra khỏi CartItem thay vì chỉ có CartItem(UserId, ProductId): giỏ
/// hàng là nơi sẽ gắn mã giảm giá, ghi chú, hạn hết giỏ - những thứ thuộc về
/// cả giỏ chứ không thuộc từng dòng. Không có thực thể Cart thì mỗi thuộc tính
/// như vậy phải nhân bản xuống mọi dòng.
/// </para>
/// </summary>
public class Cart
{
    public int Id { get; set; }

    /// <summary>
    /// Mỗi người dùng có ĐÚNG MỘT giỏ - được bảo đảm bằng unique index trên
    /// cột này, không chỉ bằng quy ước trong code.
    /// </summary>
    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Mốc để dọn giỏ bỏ quên sau này. Không có nó thì không phân biệt được giỏ
    /// vừa được sửa với giỏ bị bỏ từ hai năm trước.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    public ICollection<CartItem> Items { get; set; } = [];
}
