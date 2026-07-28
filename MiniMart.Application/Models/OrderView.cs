using MiniMart.Domain.Enums;

namespace MiniMart.Application.Models;

/// <summary>
/// Một dòng đơn hàng để hiển thị. Đọc từ dữ liệu ĐÃ SNAPSHOT trong
/// <c>OrderDetail</c>, KHÔNG join sang <c>Products</c> để lấy tên/giá hiện tại -
/// làm vậy là để lịch sử đơn hàng tự viết lại theo bảng sản phẩm.
/// </summary>
public sealed record OrderLineView(
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>
/// Đơn hàng ở dạng sẵn sàng hiển thị. Read model chứ không phải entity
/// <c>Order</c>: entity kéo theo <c>User</c> và <c>Items.Product</c>, vừa lộ dữ
/// liệu vừa dính hợp đồng vào tên property của entity.
/// </summary>
public sealed record OrderView(
    int Id,
    DateTime CreatedAt,
    decimal TotalAmount,

    /// <summary>
    /// Thêm ở Phase 9, trả một món nợ của Phase 6: cột <c>Order.Status</c> đã tồn tại
    /// từ lúc làm VNPay nhưng chưa có màn hình nào hiện nó ra, nên khách thanh toán
    /// xong không có cách nào biết hệ thống đã ghi nhận hay chưa.
    /// </summary>
    OrderStatus Status,

    ShippingInfo Shipping,
    IReadOnlyList<OrderLineView> Lines)
{
    public int TotalQuantity => Lines.Sum(l => l.Quantity);
}
