using MiniMart.Application.Models;

namespace MiniMart.Web.Models;

/// <summary>
/// Model cho <c>_CartTable.cshtml</c>.
///
/// <para>
/// <see cref="Notice"/> đi trong THÂN response chứ không qua HTTP header, vì
/// thông báo là tiếng Việt: giá trị header phải là ASCII/latin1, nhét
/// "Sản phẩm chỉ còn 3" vào header sẽ bị mã hoá sai hoặc bị chặn. Chỉ số lượng
/// (chữ số ASCII) mới đi được qua header <c>X-Cart-Count</c>.
/// </para>
/// </summary>
public sealed record CartTableViewModel(CartView Cart, string? Notice = null);
