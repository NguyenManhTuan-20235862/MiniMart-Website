namespace MiniMart.Common.Exceptions;

/// <summary>
/// Sản phẩm đã hết hàng hoàn toàn (tồn kho = 0) nên không thêm được vào giỏ.
///
/// <para>
/// CHỈ dùng cho trường hợp hết sạch. Còn hàng nhưng ít hơn số lượng yêu cầu thì
/// KHÔNG ném: <c>CartService</c> kẹp về đúng số còn lại và báo lại cho người dùng,
/// vì kẹp hữu ích hơn là từ chối rồi để họ tự đoán còn bao nhiêu.
/// </para>
/// <para>
/// Đây là ngoại lệ NGHIỆP VỤ, không phải lỗi hệ thống: nó xảy ra bình thường khi
/// người dùng mở trang từ lâu rồi mới bấm thêm vào giỏ. Controller bắt nó và
/// chuyển thành thông báo, không để nó thành HTTP 500.
/// </para>
/// </summary>
public class OutOfStockException : Exception
{
    public int ProductId { get; }

    public string ProductName { get; }

    public OutOfStockException(int productId, string productName)
        : base($"Sản phẩm '{productName}' đã hết hàng.")
    {
        ProductId = productId;
        ProductName = productName;
    }
}
