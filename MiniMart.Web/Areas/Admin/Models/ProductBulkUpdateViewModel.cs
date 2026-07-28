using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MiniMart.Web.Areas.Admin.Models;

/// <summary>
/// Model của màn hình sửa giá/tồn kho hàng loạt.
///
/// <para>
/// Toàn bộ hợp đồng giữa View và Controller nằm ở TÊN của property <see cref="Items"/>:
/// nó trở thành tiền tố của mọi input trong bảng (<c>Items[0].Price</c>,
/// <c>Items[1].Stock</c>...). Đổi tên property mà quên đổi trong Razor thì model binder
/// không tìm thấy gì, <c>Items</c> về rỗng, và Controller lưu 0 dòng - <b>không lỗi
/// nào</b>. Đây là loại hỏng chỉ test mới bắt được.
/// </para>
/// </summary>
public class ProductBulkUpdateViewModel
{
    /// <summary>
    /// Các dòng đang hiển thị và sẽ được gửi lên.
    ///
    /// <para>
    /// ★ <b>Quy tắc đánh chỉ số mà model binding đòi hỏi</b> - đây là chỗ dễ mất dữ
    /// liệu nhất của cả tính năng:
    /// </para>
    /// <list type="number">
    /// <item>
    /// Binder đọc <c>Items[0]</c>, <c>Items[1]</c>, <c>Items[2]</c>... và <b>DỪNG ở chỉ
    /// số đầu tiên bị thiếu</b>. Chỉ số phải LIÊN TỤC và BẮT ĐẦU TỪ 0.
    /// </item>
    /// <item>
    /// Hệ quả: dùng <c>ProductId</c> làm chỉ số (<c>Items[@p.Id].Price</c>) là hỏng
    /// ngay - id không bao giờ liên tục từ 0, nên binder dừng ở dòng đầu và <b>âm thầm
    /// bỏ toàn bộ phần còn lại</b>. Người dùng sửa 20 dòng, bấm Lưu, hệ thống báo thành
    /// công, và 19 dòng không được ghi.
    /// </item>
    /// <item>
    /// Vì vậy View phải dùng <c>for (var i = 0; i &lt; Model.Items.Count; i++)</c>, KHÔNG
    /// dùng <c>foreach</c>. Với <c>foreach</c> thì <c>asp-for="dong.Price"</c> sinh ra
    /// <c>name="Price"</c> không có chỉ số, mọi dòng trùng tên nhau, và binder chỉ nhận
    /// được một giá trị.
    /// </item>
    /// <item>
    /// Nếu về sau cần chỉ số KHÔNG liên tục (xoá dòng bằng JavaScript), dùng quy ước
    /// <c>Items.Index</c>: thêm
    /// <c>&lt;input type="hidden" name="Items.Index" value="k" /&gt;</c> cho mỗi dòng
    /// rồi đặt tên các ô là <c>Items[k].Price</c>. Khi đó <c>k</c> được phép là chuỗi
    /// bất kỳ. Chưa dùng vì bảng hiện tại render một lần từ server.
    /// </item>
    /// </list>
    /// <para>
    /// ⚠ Trần mặc định của binder là <b>1024 phần tử</b>
    /// (<c>MvcOptions.MaxModelBindingCollectionSize</c>). Vượt quá thì request ném
    /// <c>InvalidOperationException</c> → HTTP 500, không phải lỗi validation tử tế.
    /// Nên bảng này BẮT BUỘC phân trang; nâng trần là mời một request duy nhất giữ
    /// hàng chục nghìn object trong bộ nhớ.
    /// </para>
    /// </summary>
    public List<ProductBulkUpdateDto> Items { get; set; } = [];

    /// <summary>
    /// Trang hiện tại, đi vòng qua form để sau khi lưu còn quay lại đúng chỗ cũ.
    ///
    /// <para>
    /// Là dữ liệu ĐIỀU HƯỚNG chứ không phải dữ liệu nghiệp vụ, nên nó được bind bình
    /// thường - khác <see cref="TongSoTrang"/> ở dưới.
    /// </para>
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Tổng số trang, CHỈ để hiển thị.
    ///
    /// <para>
    /// <c>[BindNever]</c> vì nó là kết luận của server: nhận nó từ form là cho client
    /// tự khai có bao nhiêu trang. Cùng khuôn với <c>CheckoutViewModel.Cart</c>, và kèm
    /// theo cùng một hệ quả bắt buộc nhớ: <b>sau model binding nó luôn bằng 0</b>, nên
    /// đường render lại form khi có lỗi phải tự nạp lại.
    /// </para>
    /// </summary>
    [BindNever]
    public int TongSoTrang { get; set; }

    /// <summary>Tổng số sản phẩm khớp bộ lọc, CHỈ để hiển thị. Cùng lý do <c>[BindNever]</c>.</summary>
    [BindNever]
    public int TongSoSanPham { get; set; }

    public bool CoTrangTruoc => Page > 1;

    public bool CoTrangSau => Page < TongSoTrang;

    /// <summary>Không có dòng nào để sửa - View hiện thông báo thay vì một bảng rỗng.</summary>
    public bool KhongCoDong => Items.Count == 0;
}
