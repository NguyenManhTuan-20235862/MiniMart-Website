using MiniMart.Domain.Entities;

namespace MiniMart.Web.Models;

/// <summary>
/// Model của trang chi tiết sản phẩm.
///
/// <para>
/// Mang thẳng entity <see cref="Product"/> chứ không dựng DTO — đúng quy ước ở
/// <c>rules/web.md</c>: ba lý do bắt buộc dùng DTO cho JSON đều KHÔNG áp dụng cho
/// Razor. Không serialize nên không có vòng lặp tham chiếu
/// (<c>Product.Category</c> ↔ <c>Category.Products</c>); chỉ trường nào được
/// <c>@</c> trong view mới ra HTML, nên <c>Stock</c> và <c>RowVersion</c> không
/// tự rò rỉ; và view được biên dịch nên đổi tên property là lỗi build chứ không
/// phải một ô trống lúc chạy.
/// </para>
/// <para>
/// Đây là ViewModel chỉ-đọc, KHÔNG phải tham số của một action POST nào — khác
/// <c>CheckoutViewModel</c>. Vì vậy nó không cần <c>[BindNever]</c>: không có
/// đường nào để model binder chạm tới nó.
/// </para>
/// </summary>
public class ProductDetailViewModel
{
    public required Product Product { get; init; }

    /// <summary>Rỗng là kết cục bình thường: danh mục chỉ có đúng sản phẩm này.</summary>
    public required IReadOnlyList<Product> SanPhamLienQuan { get; init; }

    public bool ConHang => Product.Stock > 0;

    public bool CoGoiY => SanPhamLienQuan.Count > 0;
}
