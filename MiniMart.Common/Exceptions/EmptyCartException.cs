namespace MiniMart.Common.Exceptions;

/// <summary>
/// Đặt hàng với giỏ rỗng. Là exception chứ không phải trả về null: Controller đã
/// chặn ở <c>GET /Checkout</c>, nên nếu vẫn tới được đây thì hoặc người dùng mở hai
/// tab và đặt hai lần, hoặc giỏ vừa bị làm rỗng vì sản phẩm bị xoá khỏi shop.
/// Cả hai đều cần dừng lại và nói rõ, không được tạo một đơn 0 đồng.
/// </summary>
public class EmptyCartException : Exception
{
    public EmptyCartException()
        : base("Giỏ hàng đang trống nên chưa thể đặt hàng.")
    {
    }
}
