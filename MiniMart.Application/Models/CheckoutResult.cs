namespace MiniMart.Application.Models;

/// <summary>
/// Kết quả đặt hàng thành công. Trả read model chứ không trả entity <c>Order</c>:
/// tầng Web chỉ cần đủ để hiện trang cảm ơn, và trả entity sẽ kéo theo navigation
/// property (<c>User</c>, <c>Items.Product</c>) - vừa lộ dữ liệu vừa dính hợp đồng
/// vào tên property của entity.
/// </summary>
public sealed record CheckoutResult(int OrderId, decimal TotalAmount, int ItemCount);
