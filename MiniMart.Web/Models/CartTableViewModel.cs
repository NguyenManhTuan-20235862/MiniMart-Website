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
/// <param name="ChoPhepSua">
/// <c>true</c> ở trang <c>/Cart</c> (có ô số lượng, nút Xoá, nút Đặt hàng);
/// <c>false</c> ở trang <c>/Checkout</c> - nơi bảng chỉ để ĐỌC LẠI trước khi xác nhận.
///
/// <para>
/// Dùng cờ chứ không tạo partial thứ hai: bảng chỉ-đọc là TẬP CON của bảng sửa được,
/// nên tách file sẽ nhân đôi cách in tiền, logic badge còn/hết hàng và cấu trúc cột -
/// ba thứ chắc chắn sẽ lệch nhau về sau.
/// </para>
/// </param>
public sealed record CartTableViewModel(
    CartView Cart,
    string? Notice = null,
    bool ChoPhepSua = true);
