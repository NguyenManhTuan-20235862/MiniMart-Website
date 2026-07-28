namespace MiniMart.Domain.Entities;

/// <summary>
/// Một dòng của đơn hàng. Đối lập hoàn toàn với <see cref="CartItem"/>:
/// <c>CartItem</c> cố ý KHÔNG snapshot gì (giỏ hàng phải hiện giá hiện tại),
/// còn <c>OrderDetail</c> snapshot MỌI thứ cần để đọc lại đơn hàng.
/// </summary>
public class OrderDetail
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>
    /// Tên sản phẩm CHỐT tại thời điểm mua.
    ///
    /// <para>
    /// Snapshot cả tên chứ không chỉ giá: shop đổi tên sản phẩm ("iPhone 15" thành
    /// "iPhone 15 - hàng cũ") thì đơn hàng cũ phải hiện đúng cái tên khách đã thấy
    /// lúc mua. Không có cột này thì lịch sử đơn hàng tự viết lại theo bảng Products.
    /// </para>
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Đơn giá CHỐT tại thời điểm mua - lý do tồn tại của cả entity này.
    ///
    /// <para>
    /// Không có cột này thì hoá đơn hôm nay đọc lại tháng sau sẽ ra số khác chỉ vì
    /// shop đổi giá, và tổng tiền của đơn không còn khớp với số khách đã trả.
    /// </para>
    /// </summary>
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    /// <summary>
    /// Thành tiền dòng này. Là thuộc tính TÍNH TOÁN, không map xuống DB: nó luôn
    /// suy ra được từ hai cột đã snapshot nên lưu thêm là tạo cơ hội cho ba con số
    /// lệch nhau.
    /// </summary>
    public decimal LineTotal => UnitPrice * Quantity;
}
