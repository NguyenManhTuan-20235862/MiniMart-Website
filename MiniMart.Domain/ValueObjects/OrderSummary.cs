using MiniMart.Domain.Enums;

namespace MiniMart.Domain.ValueObjects;

/// <summary>
/// Một đơn hàng ở dạng ĐỦ ĐỂ LIỆT KÊ, không hơn.
///
/// <para>
/// Tách khỏi <c>OrderView</c> (read model của trang chi tiết) là quyết định về HIỆU
/// NĂNG, không phải về gu. Trang danh sách chỉ cần "đơn này có mấy món", còn
/// <c>OrderView</c> mang theo cả danh sách dòng đơn: dùng nó cho danh sách 10 đơn là
/// kéo về 10 × N dòng <c>OrderDetail</c> chỉ để đếm rồi vứt đi.
/// </para>
/// <para>
/// Có kiểu riêng thì repository mới <b>chiếu</b> (project) được xuống đúng các cột cần,
/// và <c>TongSoLuong</c> được SQL Server tính bằng một subquery <c>SUM</c> - không dòng
/// <c>OrderDetail</c> nào rời khỏi database.
/// </para>
/// <para>
/// Đặt ở Domain chứ không Application vì <c>IOrderRepository</c> (Domain) là nơi khai
/// báo nó trong chữ ký. Cùng chỗ với <c>CartLine</c>, và cùng lý do.
/// </para>
/// </summary>
public sealed record OrderSummary(
    int Id,
    DateTime CreatedAt,
    decimal TotalAmount,
    OrderStatus Status,
    int TongSoLuong);
