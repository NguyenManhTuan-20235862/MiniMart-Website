namespace MiniMart.Domain.Entities;

/// <summary>
/// Một dòng trong giỏ hàng. Mỗi sản phẩm chỉ được có MỘT dòng trong cùng một
/// giỏ - ràng buộc bằng unique index (CartId, ProductId), xem CartItemConfiguration.
///
/// <para>
/// Chính ràng buộc đó là thứ cho phép API giỏ hàng dùng <c>productId</c> làm khoá
/// thay vì <c>cartItemId</c>: đã duy nhất thì productId định danh được dòng.
/// </para>
/// </summary>
public class CartItem
{
    public int Id { get; set; }

    public int CartId { get; set; }

    public Cart Cart { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    // CỐ Ý KHÔNG lưu giá tại thời điểm thêm vào giỏ.
    //
    // Giỏ hàng phải hiển thị giá HIỆN TẠI: người dùng thanh toán theo giá lúc
    // đặt hàng, không phải giá lúc họ bấm "thêm vào giỏ" ba tuần trước. Chốt giá
    // là việc của OrderItem ở phase đặt hàng - lúc đó mới BẮT BUỘC snapshot, vì
    // hoá đơn đã phát hành không được đổi khi shop chỉnh bảng giá.
    //
    // Cũng KHÔNG có RowVersion: xung đột giỏ hàng (hai tab cùng sửa) để
    // "ghi sau thắng" là chấp nhận được. Bắt người dùng xử lý xung đột khi thêm
    // hàng vào giỏ là phiền toái không tương xứng với rủi ro.
}
