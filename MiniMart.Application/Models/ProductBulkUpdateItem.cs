namespace MiniMart.Application.Models;

/// <summary>
/// Một dòng của thao tác sửa giá / tồn kho hàng loạt.
///
/// <para>
/// Tách khỏi <c>ProductBulkUpdateDto</c> ở tầng Web vì đó là một ViewModel: nó mang
/// theo <c>Name</c> để hiển thị, mang Data Annotation để dựng thông báo lỗi, và có
/// <c>[BindNever]</c> - toàn những thứ chỉ có nghĩa với model binding. Application
/// không được biết đến ASP.NET Core, và một record bốn trường thì rẻ hơn nhiều so với
/// việc kéo cả tầng Web vào Application để dùng lại một class.
/// </para>
/// <para>
/// <paramref name="RowVersion"/> null nghĩa là <b>bỏ qua</b> kiểm tra xung đột - cùng
/// quy ước với <c>IProductService.UpdateAsync</c>, dành cho luồng nội bộ không có form
/// (job, seed). Mọi đường đi qua form BẮT BUỘC gửi nó lên.
/// </para>
/// </summary>
public record ProductBulkUpdateItem(int Id, decimal Price, int Stock, byte[]? RowVersion);
