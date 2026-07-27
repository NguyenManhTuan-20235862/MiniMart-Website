using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MiniMart.Application.Models;

namespace MiniMart.Web.Areas.Admin.Models;

/// <summary>
/// MỘT dòng của bảng sửa hàng loạt: đúng những gì người dùng được phép đổi trên một
/// sản phẩm, không hơn.
///
/// <para>
/// ★ Tập property ở đây CHÍNH LÀ hàng rào chống over-posting, và nó là hàng rào theo
/// CẤU TRÚC chứ không phải theo một câu <c>if</c>. Không có <c>Name</c>, không có
/// <c>CategoryId</c>, không có <c>ImageUrl</c> - nên không tồn tại cách nào để một
/// request đổi tên sản phẩm qua màn hình này, kể cả khi người gửi tự chế thêm trường.
/// Thêm một property vào đây là mở thêm một quyền, không phải "cho tiện".
/// </para>
/// <para>
/// Là kiểu <b>class</b> chứ không phải <c>record</c> positional: model binder cần
/// constructor không tham số và property có <c>set</c>. Record positional sẽ bind ra
/// toàn giá trị mặc định - im lặng, không lỗi nào.
/// </para>
/// </summary>
public class ProductBulkUpdateDto
{
    /// <summary>
    /// Sản phẩm nào. BẮT BUỘC render ra hidden field trong mỗi dòng - thiếu nó thì
    /// binder cho ra <c>Id = 0</c> và dòng đó mất danh tính hoàn toàn.
    ///
    /// <para>
    /// Nhận <c>Id</c> từ form KHÔNG phải lỗ hổng ở màn hình này: đây là khu vực Admin,
    /// người dùng vốn có quyền sửa mọi sản phẩm. Khác hẳn giỏ hàng - ở đó chủ sở hữu
    /// phải đến từ cookie nên endpoint cố ý chỉ nhận <c>productId</c>, xem
    /// <c>rules/cart.md</c>.
    /// </para>
    /// </summary>
    public int Id { get; set; }

    // Dùng overload typeof(decimal) + chuỗi thay vì Range(0.01, ...) vì overload kia
    // nhận double, làm tròn nhị phân trước khi so sánh với decimal.
    //
    // ★ CẦN CẢ HAI cờ culture, và đây là chỗ quy ước cũ của dự án đã SAI:
    //
    //   ParseLimitsInInvariantCulture  -> parse hai chuỗi CẬN "0.01"/"999999999"
    //   ConvertValueInInvariantCulture -> chuyển đổi GIÁ TRỊ đang được kiểm
    //
    // Chỉ đặt cờ thứ hai (như code cũ) thì cận vẫn được parse theo CurrentCulture, và
    // trên máy vi-VN "0.01" ném ArgumentException ngay trong lúc validate -> HTTP 500.
    // Đã đo trực tiếp: chỉ ConvertValue -> NÉM; chỉ ParseLimits -> chạy đúng.
    //
    // Giữ ĐÚNG cùng ràng buộc với ProductFormViewModel: hai màn hình cùng ghi vào một
    // cột mà đặt hai giới hạn khác nhau thì cột đó thực chất không có giới hạn nào.
    [Range(typeof(decimal), "0.01", "999999999",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true,
        ErrorMessage = "Giá phải lớn hơn 0.")]
    [Display(Name = "Giá (VNĐ)")]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Tồn kho không được âm.")]
    [Display(Name = "Tồn kho")]
    public int Stock { get; set; }

    /// <summary>
    /// Phiên bản bản ghi lúc bảng được MỞ - mỗi dòng một giá trị riêng.
    ///
    /// <para>
    /// Đây là điểm khiến sửa hàng loạt khó hơn sửa lẻ: một lần POST mang theo N phiên
    /// bản độc lập, và mỗi dòng có thể xung đột riêng. Không có cột này thì Optimistic
    /// Concurrency biến mất và một lần bấm "Lưu tất cả" sẽ ghi đè lặng lẽ mọi thay đổi
    /// mà người khác vừa thực hiện trên BẤT KỲ dòng nào đang hiển thị.
    /// </para>
    /// <para>
    /// ⚠ <c>byte[]</c> được model binder mặc định giải mã từ Base64, nhưng View
    /// <b>TUYỆT ĐỐI không được dùng <c>asp-for</c></b> cho nó: <c>InputTagHelper</c>
    /// gọi <c>ToString()</c> trên mảng byte và render ra chuỗi <c>"System.Byte[]"</c>.
    /// Form vẫn submit, không lỗi nào, và concurrency biến mất trong im lặng. Phải
    /// render thủ công bằng <c>Convert.ToBase64String</c> - xem
    /// <c>.claude/rules/concurrency.md</c>.
    /// </para>
    /// <para>
    /// Nullable: <c>null</c> nghĩa là "bỏ qua kiểm tra phiên bản", dành cho luồng nội
    /// bộ không có form. Là chủ ý, giống <c>ProductFormViewModel.RowVersion</c>.
    /// </para>
    /// </summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Tên sản phẩm, CHỈ để người dùng biết mình đang sửa dòng nào.
    ///
    /// <para>
    /// <c>[BindNever]</c> giữ nguyên tính chất chống over-posting đã nêu ở đầu class:
    /// nó có mặt trên đường server → view, nhưng không tồn tại đường ngược lại. Thêm
    /// <c>&lt;input asp-for="Items[i].Name"&gt;</c> vào View cũng không đổi được gì -
    /// binder bỏ qua hoàn toàn.
    /// </para>
    /// <para>
    /// Hệ quả bắt buộc nhớ: sau model binding, property này RỖNG. Đường render lại
    /// bảng khi có lỗi validation phải tự nạp lại tên từ DB.
    /// </para>
    /// </summary>
    [BindNever]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Có, và chỉ có, khi dòng này vừa bị BỎ QUA ở lần lưu trước vì người khác đã sửa
    /// nó. <c>null</c> là trường hợp thường.
    ///
    /// <para>
    /// Mang cả object <c>ProductConflict</c> thay vì một cờ <c>bool</c>: thứ Admin cần
    /// để quyết định không phải "có xung đột" mà là <b>người kia đã đổi thành giá trị
    /// gì</b>. Một cờ bool sẽ đẩy việc đó về câu thông báo chung ở đầu trang, tức bắt họ
    /// đối chiếu bằng mắt giữa một đoạn văn và 20 dòng bảng.
    /// </para>
    /// <para>
    /// <c>[BindNever]</c> vì đây là kết luận của server. Test cấu trúc
    /// <c>Dong_sua_hang_loat_chi_BIND_duoc_dung_bon_truong</c> vẫn xanh đúng như thiết
    /// kế: nó đếm các property BIND ĐƯỢC, nên trường chỉ-để-hiển-thị không làm rộng bề
    /// mặt tấn công.
    /// </para>
    /// </summary>
    [BindNever]
    public ProductConflict? XungDot { get; set; }
}
