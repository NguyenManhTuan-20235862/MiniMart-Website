using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using MiniMart.Application.Models;

namespace MiniMart.Web.Models;

/// <summary>
/// Model cho trang <c>/Checkout</c>: vừa là dữ liệu để HIỂN THỊ lại giỏ hàng, vừa là
/// form nhận thông tin giao hàng.
///
/// <para>
/// Một type cho cả hai chiều là chủ ý. Tách làm hai (một để render, một để POST) thì
/// khi form không hợp lệ phải ghép lại thủ công, và mọi đường ghép thủ công đều có
/// nhánh quên giữ dữ liệu người dùng vừa gõ.
/// </para>
/// <para>
/// ⚠ Vì vậy nó là một type <b>lai</b>, và chỗ nguy hiểm nằm ở <see cref="Cart"/> - xem
/// chú thích của property đó.
/// </para>
/// </summary>
public class CheckoutViewModel
{
    /// <summary>
    /// Giỏ hàng để hiển thị lại. CHỈ đi từ server ra view, KHÔNG bao giờ đi ngược.
    ///
    /// <para>
    /// <c>[BindNever]</c> là thứ giữ điều đó đúng, và nó không phải trang trí:
    /// <c>CheckoutViewModel</c> vừa là model của view vừa là tham số của action POST,
    /// nên nếu thiếu nó thì model binder sẵn sàng nhận
    /// <c>Cart.Lines[0].UnitPrice=1</c> từ form. Hôm nay chưa gây hại vì
    /// <c>CheckoutAsync</c> đọc giỏ từ DB chứ không từ model này - nhưng đó là an toàn
    /// nhờ MAY MẮN của thứ tự đọc, không phải nhờ cấu trúc. <c>[BindNever]</c> biến nó
    /// thành an toàn nhờ cấu trúc: không tồn tại đường để giá trị từ form chạm vào đây.
    /// </para>
    /// <para>
    /// Hệ quả bắt buộc nhớ: sau mỗi lần model binding, property này RỖNG. Đường render
    /// lại form phải tự nạp lại nó từ <c>ICartService</c> - đúng khuôn "ModelState
    /// không hợp lệ thì nhớ nạp lại dropdown/SelectList" trong <c>rules/web.md</c>.
    /// </para>
    /// </summary>
    [BindNever]
    [ValidateNever]
    public CartView Cart { get; set; } = CartView.Empty;

    /// <summary>
    /// Tên NGƯỜI NHẬN, không phải tên tài khoản. Không tự điền từ <c>ICurrentUser</c>:
    /// mua tặng hoặc giao tới cơ quan là chuyện bình thường.
    /// </summary>
    [Display(Name = "Họ tên người nhận")]
    [Required(ErrorMessage = "Vui lòng nhập họ tên người nhận.")]
    [StringLength(100, ErrorMessage = "Họ tên tối đa {1} ký tự.")]
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>
    /// Số điện thoại người nhận.
    ///
    /// <para>
    /// Regex cố ý DỄ DÃI (chữ số, dấu cách, <c>+</c>, <c>-</c>, <c>()</c>, <c>.</c>) chứ
    /// không khoá theo đầu số Việt Nam. Đầu số mới được cấp thêm theo thời gian, và một
    /// regex chặt sẽ âm thầm từ chối khách hàng thật - hỏng theo hướng đắt hơn nhiều so
    /// với việc lọt một chuỗi vô nghĩa mà nhân viên giao hàng sẽ phát hiện ngay.
    /// </para>
    /// <para>
    /// Cùng lý do với việc mật khẩu không đòi ký tự đặc biệt: quy tắc càng chặt thì
    /// người dùng càng bị đẩy vào chỗ nhập bừa cho qua.
    /// </para>
    /// </summary>
    [Display(Name = "Số điện thoại")]
    [Required(ErrorMessage = "Vui lòng nhập số điện thoại người nhận.")]
    [StringLength(20, MinimumLength = 8, ErrorMessage = "Số điện thoại phải từ {2} đến {1} ký tự.")]
    [RegularExpression(@"^[0-9+()\s.\-]+$",
        ErrorMessage = "Số điện thoại chỉ gồm chữ số và các ký tự + - ( ) khoảng trắng.")]
    public string RecipientPhone { get; set; } = string.Empty;

    /// <summary>
    /// Địa chỉ giao hàng. Một ô văn bản duy nhất, KHÔNG tách tỉnh/huyện/xã: tách ra
    /// đúng thì cần dữ liệu hành chính thật kèm dropdown phụ thuộc nhau, mà danh mục
    /// đó còn thay đổi theo các đợt sáp nhập. Một ô tự do giao được hàng ngay hôm nay;
    /// tách ô là việc của lúc tích hợp đơn vị vận chuyển.
    /// </summary>
    /// <summary>
    /// Người dùng bấm nút nào để gửi form.
    ///
    /// <para>
    /// ⚠ Hôm nay nó KHÔNG được lưu xuống DB, và đó là chủ ý. Đơn hàng chỉ có
    /// <c>Status</c> (<c>Pending</c>/<c>Paid</c>); lựa chọn này chỉ quyết định người
    /// dùng được đưa đi đâu SAU KHI đơn đã tạo. Thêm cột <c>PaymentMethod</c> ngay bây
    /// giờ là lặp lại đúng cái sai đã tránh hai lần: thêm cột trước khi có nghiệp vụ
    /// đọc nó. Khi có báo cáo doanh thu theo phương thức thì mới thêm.
    /// </para>
    /// <para>
    /// KHÔNG có <c>[Required]</c>, và mặc định an toàn được giữ bằng HAI lớp: property
    /// initializer dưới đây (lớp thật sự đang chạy), và <c>Cod = 0</c> trong enum (lớp
    /// dự phòng nếu initializer bị xoá). Thiếu tham số thì đặt hàng bình thường, chứ
    /// không đẩy khách sang cổng thanh toán ngoài ý muốn.
    /// </para>
    /// </summary>
    public PhuongThucThanhToan PhuongThuc { get; set; } = PhuongThucThanhToan.Cod;

    [Display(Name = "Địa chỉ giao hàng")]
    [Required(ErrorMessage = "Vui lòng nhập địa chỉ giao hàng.")]
    [StringLength(300, MinimumLength = 10,
        ErrorMessage = "Địa chỉ phải từ {2} đến {1} ký tự (số nhà, đường, phường/xã, tỉnh/thành).")]
    public string ShippingAddress { get; set; } = string.Empty;
}
