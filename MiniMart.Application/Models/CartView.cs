namespace MiniMart.Application.Models;

/// <summary>
/// Một dòng giỏ hàng đã được LÀM GIÀU: ghép <c>CartLine</c> (chỉ có ProductId,
/// Quantity) với dữ liệu sản phẩm đọc từ bảng Products.
///
/// <para>
/// <see cref="UnitPrice"/> là giá HIỆN TẠI, không phải giá lúc thêm vào giỏ -
/// đúng với việc <c>CartItem</c> cố ý không snapshot giá. Chốt giá là việc của
/// <c>OrderItem</c> ở phase đặt hàng.
/// </para>
/// <para>
/// <see cref="AvailableStock"/> có mặt để giao diện kẹp được ô nhập số lượng và
/// để giải thích khi số lượng bị điều chỉnh. Đây là dữ liệu cho tầng hiển thị
/// dùng, KHÔNG in con số đó ra HTML - quy ước cũ vẫn giữ: chỉ hiện trạng thái
/// còn/hết hàng.
/// </para>
/// </summary>
public sealed record CartLineView(
    int ProductId,
    string Name,
    string? ImageUrl,
    decimal UnitPrice,
    int Quantity,
    int AvailableStock)
{
    public decimal LineTotal => UnitPrice * Quantity;

    public bool InStock => AvailableStock > 0;

    /// <summary>
    /// Số lượng trong giỏ đã vượt tồn kho hiện tại. Xảy ra khi có người khác mua
    /// bớt sau lúc dòng này được thêm vào giỏ - giỏ hàng KHÔNG giữ hàng.
    /// </summary>
    public bool ExceedsStock => Quantity > AvailableStock;
}

/// <summary>
/// Toàn bộ giỏ hàng ở dạng sẵn sàng hiển thị. Cùng một kiểu cho cả giỏ Session
/// và giỏ DB - đó là mục đích của việc tách <c>ICartStore</c>.
/// </summary>
public sealed record CartView(IReadOnlyList<CartLineView> Lines)
{
    public static CartView Empty { get; } = new([]);

    public bool IsEmpty => Lines.Count == 0;

    /// <summary>Tổng số món, dùng cho badge trên navbar (không phải số dòng).</summary>
    public int TotalQuantity => Lines.Sum(l => l.Quantity);

    public decimal TotalAmount => Lines.Sum(l => l.LineTotal);

    /// <summary>Có dòng nào đang vượt tồn kho - giao diện cần cảnh báo trước khi đặt hàng.</summary>
    public bool HasStockProblem => Lines.Any(l => l.ExceedsStock);
}

/// <summary>
/// Kết quả một thao tác ghi lên giỏ hàng.
///
/// <para>
/// Có <see cref="Notice"/> vì thao tác có thể THÀNH CÔNG MỘT PHẦN: yêu cầu thêm
/// 10 mà chỉ còn 3 thì kẹp về 3 và phải nói cho người dùng biết. Trả về
/// <c>CartView</c> trần thì thông tin đó mất, và người dùng thấy số lượng khác số
/// mình vừa nhập mà không hiểu vì sao.
/// </para>
/// </summary>
public sealed record CartMutationResult(CartView Cart, string? Notice = null);
