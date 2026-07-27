using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniMart.Application.Interfaces;
using MiniMart.Domain.Interfaces;
using MiniMart.Web.Middleware;
using MiniMart.Web.Models;

namespace MiniMart.Web.Controllers;

/// <summary>
/// Nơi VNPay đưa trình duyệt của khách quay về sau khi thanh toán.
///
/// <para>
/// ★★ RÀNG BUỘC QUAN TRỌNG NHẤT CỦA CẢ FILE: action ở đây <b>KHÔNG ĐƯỢC GHI DB</b>.
/// Không cập nhật trạng thái đơn, không trừ kho, không ghi bản ghi thanh toán. Nó chỉ
/// đọc chữ ký rồi hiển thị một câu cho khách xem. Lý do nằm ở chú thích của
/// <see cref="Return"/> - đọc trước khi định "tiện tay cập nhật luôn".
/// </para>
/// <para>
/// ⚠ Ràng buộc đó TRƯỚC ĐÂY được bảo đảm bằng cấu trúc: constructor chỉ nhận
/// <see cref="IVnPayService"/> nên không tồn tại đường nào để ghi DB. Từ khi có
/// <see cref="IpnAction"/>, controller này buộc phải giữ thêm
/// <see cref="IPaymentService"/> - tức bảo đảm cấu trúc đó KHÔNG còn.
/// </para>
/// <para>
/// Đổi lại bằng một bảo đảm khác và mạnh hơn: <c>Order.Status</c> giờ đã tồn tại, nên
/// có test HÀNH VI khẳng định <see cref="Return"/> với chữ ký hợp lệ báo thành công
/// vẫn để đơn ở <c>Pending</c> và không tạo bản ghi <c>Payment</c> nào. Trước đây
/// không viết được test đó vì chưa có trạng thái nào để quan sát.
/// </para>
/// </summary>
[Authorize]
public class PaymentController : Controller
{
    private readonly IVnPayService _vnPayService;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(
        IVnPayService vnPayService,
        IPaymentService paymentService,
        ILogger<PaymentController> logger)
    {
        _vnPayService = vnPayService;
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>
    /// Hiển thị kết quả thanh toán cho khách. <b>Chỉ để xem.</b>
    ///
    /// <para>
    /// <b>Vì sao action này KHÔNG được dùng để xác nhận đơn hàng</b> - bốn lý do, và
    /// lý do đầu là thứ khiến ba lý do sau trở nên không cứu vãn được:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>Nó có thể KHÔNG BAO GIỜ xảy ra.</b> Đây là một lần chuyển hướng của TRÌNH
    /// DUYỆT. Khách trả tiền xong rồi đóng tab, mất mạng, hết pin - tiền đã trừ mà
    /// request này không tới. Nếu đây là nơi ghi nhận thanh toán thì đơn đó vĩnh viễn
    /// nằm ở trạng thái "chưa trả" trong khi ngân hàng đã trừ tiền. Không có cách nào
    /// sửa bằng code ở đây, vì code ở đây không chạy.
    /// </item>
    /// <item>
    /// <b>Nó có thể xảy ra NHIỀU LẦN.</b> Bấm F5 là gửi lại đúng URL đó, chữ ký vẫn
    /// hợp lệ. Ghi DB ở đây thì mỗi lần F5 là một lần ghi nhận thanh toán.
    /// </item>
    /// <item>
    /// <b>Thời điểm không đáng tin.</b> URL này nằm trong lịch sử trình duyệt và có
    /// thể được mở lại sau nhiều ngày. Chữ ký vẫn đúng - chữ ký chứng minh dữ liệu do
    /// VNPay tạo ra, nó KHÔNG chứng minh "vừa mới xảy ra".
    /// </item>
    /// <item>
    /// <b>Đã có kênh đúng cho việc đó: IPN</b> (Instant Payment Notification) - VNPay
    /// gọi thẳng từ máy chủ của họ sang máy chủ của ta, không đi qua trình duyệt khách.
    /// Kênh đó tự thử lại khi thất bại và không phụ thuộc việc khách còn mở trình duyệt
    /// hay không. <b>Đó</b> mới là nơi ghi nhận thanh toán.
    /// </item>
    /// </list>
    /// <para>
    /// Nói gọn: kênh này trả lời <i>"hiện cho khách xem cái gì"</i>, kênh IPN trả lời
    /// <i>"sự thật là gì"</i>. Trộn hai câu hỏi vào một chỗ là cách kinh điển để có
    /// những đơn đã thu tiền mà hệ thống không biết.
    /// </para>
    /// <para>
    /// ⚠ IPN <b>chưa được làm</b> - xem nợ kỹ thuật trong <c>claude.md</c>. Cho tới lúc
    /// đó, không có đơn nào được đánh dấu đã thanh toán, và điều này ĐÚNG hơn hẳn việc
    /// đánh dấu từ đây rồi tin nhầm.
    /// </para>
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Return()
    {
        // AllowAnonymous, khác [Authorize] ở cấp class. Lý do: khách quay về từ một
        // site khác và phiên đăng nhập có thể đã hết hạn trong lúc họ thao tác ở ngân
        // hàng. Đẩy họ sang trang đăng nhập ngay sau khi vừa trả tiền là cách chắc
        // chắn nhất khiến họ tưởng giao dịch hỏng.
        //
        // An toàn vì trang này KHÔNG hiện dữ liệu riêng tư nào: chỉ một câu trạng thái
        // và mã đơn. Chi tiết đơn nằm sau /Checkout/Success/{id}, nơi vẫn [Authorize]
        // và vẫn lọc theo chủ sở hữu.
        var thamSo = Request.Query.ToDictionary(
            cap => cap.Key,
            cap => (string?)cap.Value.ToString(),
            StringComparer.Ordinal);

        // ★ KIỂM CHỮ KÝ TRƯỚC, luôn luôn. Mọi trường khác trong query string đều là
        // chuỗi do người gửi tự đặt cho tới khi chữ ký được xác nhận - kể cả
        // vnp_ResponseCode. Đọc mã kết quả rồi mới kiểm chữ ký là đã tin vào dữ liệu
        // chưa xác thực, dù chỉ trong vài dòng.
        var ketQua = _vnPayService.Verify(thamSo);

        if (!ketQua.ChuKyHopLe)
        {
            // KHÔNG nói "sai chữ ký" cho người dùng cuối: với khách thật thì câu đó vô
            // nghĩa, còn với người đang dò thì nó là phản hồi giúp họ dò tiếp.
            return View(VnPayReturnViewModel.KhongXacThucDuoc);
        }

        return View(VnPayReturnViewModel.Tu(ketQua));
    }

    /// <summary>
    /// Kênh IPN: VNPay gọi THẲNG từ máy chủ của họ sang máy chủ của ta.
    ///
    /// <para>
    /// Đây mới là <b>nguồn sự thật</b> về việc thanh toán, đối lập hoàn toàn với
    /// <see cref="Return"/>. Nó không đi qua trình duyệt khách nên không mất khi khách
    /// đóng tab, và VNPay tự gửi lại cho tới khi nhận được phản hồi hợp lệ.
    /// </para>
    /// <para>
    /// Controller ở đây vẫn KHÔNG chứa nghiệp vụ - nó chuyển query string cho
    /// <c>IPaymentService</c> rồi dịch kết quả sang JSON. Toàn bộ bốn lệnh kiểm (chữ
    /// ký, đơn tồn tại, số tiền, đã ghi nhận chưa) nằm ở tầng Application, nơi test
    /// được bằng Moq mà không cần bắn HTTP.
    /// </para>
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    // Người gọi là MÁY CHỦ VNPay, không phải trình duyệt, và nó không cam kết gửi
    // Accept: application/json. Thiếu attribute này thì một lỗi chưa xử lý sẽ trả về
    // trang HTML tiếng Việt cho một chương trình đang chờ JSON.
    [JsonErrorResponse]
    public async Task<IActionResult> IpnAction(CancellationToken cancellationToken)
    {
        // AllowAnonymous là BẮT BUỘC, không phải tiện tay: người gọi là máy chủ VNPay,
        // nó không có và không thể có cookie đăng nhập nào. Thứ xác thực request này
        // là CHỮ KÝ HMAC - và đó là một cơ chế xác thực đầy đủ, mạnh hơn cookie ở chỗ
        // nó không thể bị đánh cắp bằng XSS.
        //
        // Không cần [IgnoreAntiforgeryToken]: antiforgery chỉ áp cho POST/PUT/DELETE.
        // GET ở đây đúng vì VNPay đặc tả IPN là GET; và khác Return, action này CÓ ghi
        // DB - an toàn nhờ chữ ký, không nhờ phương thức HTTP.
        var thamSo = Request.Query.ToDictionary(
            cap => cap.Key,
            cap => (string?)cap.Value.ToString(),
            StringComparer.Ordinal);

        // Dòng "đã có request tới" phải nằm ở ĐÂY, không phải trong PaymentService:
        // đây là biên HTTP, và nếu ta ghi ở tầng dưới thì mọi request bị chặn TRƯỚC đó
        // (rate limit, exception lúc bind) sẽ không để lại dấu vết nào. Khi VNPay báo
        // "chúng tôi đã gọi IPN" thì đây là bằng chứng đối chứng duy nhất.
        //
        // ⚠ Chỉ ghi ĐỊA CHỈ IP và SỐ LƯỢNG tham số, TUYỆT ĐỐI không ghi cả query string.
        // Ở thời điểm này chưa có gì được xác thực - mọi ký tự đều do người gửi tự đặt,
        // nên đổ nguyên vào log là mở đường cho họ bơm nội dung tuỳ ý vào file log của
        // ta (giả mạo dòng log, hoặc nhồi payload cho công cụ đọc log ở phía sau).
        // Giá trị thật của các tham số được PaymentService ghi SAU khi kiểm chữ ký.
        _logger.LogInformation(
            "Nhận IPN từ {DiaChiIp} với {SoThamSo} tham số.",
            HttpContext.Connection.RemoteIpAddress, thamSo.Count);

        var ketQua = await _paymentService.XuLyIpnAsync(thamSo, cancellationToken);

        // Ghi lại KẾT CỤC kèm mã trả về cho VNPay. Không có dòng này thì log có "đã
        // nhận" mà không có "đã trả lời gì", và khi đối soát thì không phân biệt được
        // "ta từ chối" với "ta chết giữa chừng".
        _logger.LogInformation(
            "Trả lời IPN: RspCode {RspCode} - {Message}.", ketQua.RspCode, ketQua.Message);

        // LUÔN trả HTTP 200 kèm JSON, kể cả khi từ chối. Đây là ngoại lệ có chủ đích
        // với thói quen "lỗi thì trả 4xx": VNPay đọc mã trong THÂN response
        // (RspCode), không đọc mã HTTP. Trả 400 cho một chữ ký sai sẽ khiến họ coi là
        // lỗi tầng vận chuyển và gửi lại mãi một thông báo không bao giờ hợp lệ.
        //
        // Json() chứ không PartialView(): đây đúng là trường hợp mà quy ước
        // "AJAX thì trả PartialView" đã nêu ngoại lệ - client KHÔNG phải trình duyệt.
        return Json(ketQua);
    }
}
