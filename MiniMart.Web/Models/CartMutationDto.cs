using MiniMart.Application.Models;
using MiniMart.Web.Extensions;

namespace MiniMart.Web.Models;

/// <summary>
/// Hợp đồng JSON cho ba endpoint ghi của giỏ hàng, dùng bởi dropdown trên navbar.
///
/// <para>
/// Đây là NGOẠI LỆ có lý do với quy ước "AJAX thì trả PartialView" (xem
/// <c>.claude/rules/web.md</c>). Dropdown chỉ cần đổi vài CON SỐ tại chỗ - số lượng
/// của một dòng, thành tiền dòng đó, tổng cộng, badge. Gửi lại cả khối HTML để
/// thay <c>innerHTML</c> sẽ làm dropdown nhấp nháy và mất vị trí cuộn.
/// </para>
/// <para>
/// Nhưng LÝ DO của quy ước vẫn được giữ nguyên bằng hai ràng buộc:
/// </para>
/// <para>
/// 1. Mọi số tiền đi kèm dạng CHUỖI ĐÃ ĐỊNH DẠNG SẴN (<c>TotalText</c>,
///    <c>LineTotalText</c>) bởi <c>MoneyFormat</c>. JavaScript KHÔNG được tự định dạng
///    tiền - nếu không thì cách in tiền tồn tại ở hai nơi và sẽ lệch nhau.
/// </para>
/// <para>
/// 2. DTO chỉ mang dữ liệu để GÁN vào node có sẵn, không đủ để dựng markup. Việc dựng
///    HTML vẫn thuộc Razor: <c>GET /Cart/Dropdown</c> trả <c>_CartDropdown</c>.
///    Nhờ vậy markup thẻ sản phẩm vẫn chỉ được định nghĩa MỘT lần.
/// </para>
/// </summary>
public sealed record CartMutationDto(
    int Count,
    decimal Total,
    string TotalText,
    bool IsEmpty,
    string? Notice,
    CartLineChangeDto Line)
{
    /// <param name="productId">
    /// Sản phẩm vừa bị tác động. Chỉ trả về DÒNG NÀY chứ không trả cả giỏ: client chỉ
    /// cần sửa đúng một dòng, và trả cả giỏ sẽ dụ người viết JS dựng lại danh sách
    /// bằng JavaScript - đúng thứ quy ước muốn tránh.
    /// </param>
    public static CartMutationDto From(CartView cart, int productId, string? notice)
    {
        var line = cart.Lines.FirstOrDefault(l => l.ProductId == productId);

        return new CartMutationDto(
            cart.TotalQuantity,
            cart.TotalAmount,
            cart.TotalAmount.ToMoneyText(),
            cart.IsEmpty,
            notice,
            line is null
                // Không còn trong giỏ: vừa bị xoá, hoặc sản phẩm đã bị xoá khỏi shop,
                // hoặc số lượng được đặt về 0. Với client cả ba đều là "bỏ dòng này đi".
                ? new CartLineChangeDto(productId, 0, string.Empty, Removed: true)
                : new CartLineChangeDto(
                    line.ProductId,
                    line.Quantity,
                    line.LineTotal.ToMoneyText(),
                    Removed: false));
    }
}

/// <summary>
/// Dòng vừa thay đổi. KHÔNG mang <c>Stock</c>: quy ước của dự án là hiển thị TRẠNG THÁI
/// còn/hết hàng chứ không phải con số tồn kho, và nút "+" cũng không cần biết trần -
/// nó cứ gửi, server kẹp lại rồi báo về qua <see cref="CartMutationDto.Notice"/>.
/// </summary>
public sealed record CartLineChangeDto(
    int ProductId,
    int Quantity,
    string LineTotalText,
    bool Removed);
