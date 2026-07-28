using MiniMart.Application.Models;
using MiniMart.Web.Extensions;

namespace MiniMart.Web.Models;

/// <summary>
/// Hợp đồng JSON cho <c>/Cart/Summary</c> - dữ liệu cho badge trên navbar.
///
/// <para>
/// Đây là ngoại lệ CÓ CHỦ ĐÍCH với quy ước "AJAX thì trả PartialView": client cần
/// một CON SỐ để hiển thị, không cần một khối HTML. Trả HTML cho badge là gửi cả
/// một fragment chỉ để lấy ra chữ số bên trong.
/// </para>
/// <para>
/// DTO riêng chứ không trả thẳng <see cref="CartView"/>: CartView mang cả danh
/// sách dòng kèm tên, giá, tồn kho từng sản phẩm - badge không cần gì trong đó,
/// và gửi ra là lộ dữ liệu không cần thiết.
/// </para>
/// </summary>
public sealed record CartSummaryDto(int Count, decimal Total, string TotalText)
{
    public static CartSummaryDto From(CartView cart) =>
        // TotalText được server định dạng sẵn để client không phải tự làm và tự
        // lệch khỏi cách server in tiền (MoneyFormat khoá InvariantCulture).
        new(cart.TotalQuantity, cart.TotalAmount, cart.TotalAmount.ToMoneyText());
}
