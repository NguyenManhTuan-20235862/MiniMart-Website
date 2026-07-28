using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniMart.Application.Interfaces;
using MiniMart.Application.Models;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Interfaces;
using MiniMart.Web.Models;

namespace MiniMart.Web.Controllers;

/// <summary>
/// Bước xem lại giỏ hàng trước khi xác nhận đặt hàng.
///
/// <para>
/// <c>[Authorize]</c> đặt ở cấp CLASS, khác hẳn <c>CartController</c>: khách vãng lai
/// phải mua được hàng, nhưng ĐẶT hàng thì cần một tài khoản để gắn đơn vào. Đặt ở cấp
/// class nên action thêm sau (POST xác nhận, trang cảm ơn) tự động được bảo vệ.
/// </para>
/// <para>
/// Khách vãng lai bị framework đẩy sang <c>/Account/Login?ReturnUrl=%2FCheckout</c>.
/// Luồng sau đó đã chạy sẵn từ Phase 4 và không cần thêm gì: đăng nhập xong,
/// <c>SignInUserAsync</c> gộp giỏ Session vào giỏ DB, rồi <c>RedirectToLocal</c> đưa họ
/// về đúng <c>/Checkout</c> với giỏ hàng đầy đủ.
/// </para>
/// </summary>
[Authorize]
public class CheckoutController : Controller
{
    private readonly ICartService _cartService;
    private readonly IOrderService _orderService;
    private readonly IPaymentService _paymentService;
    private readonly ICurrentUser _currentUser;

    // Chỉ inject abstraction. Controller không biết giỏ nằm ở Session hay DB - với
    // trang này thì luôn là DB (vì [Authorize]), nhưng đó là kết luận của factory ở
    // Program.cs, không phải điều Controller được phép giả định.
    public CheckoutController(
        ICartService cartService,
        IOrderService orderService,
        IPaymentService paymentService,
        ICurrentUser currentUser)
    {
        _cartService = cartService;
        _orderService = orderService;
        _paymentService = paymentService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Id người đang đăng nhập, đọc từ COOKIE ĐÃ KÝ.
    ///
    /// <para>
    /// ★ Đây là chỗ duy nhất trong luồng đặt hàng lấy ra <c>userId</c>, và nó TUYỆT
    /// ĐỐI không được đến từ form hay query string. Nhận từ request là mở thẳng một
    /// lỗ IDOR: đặt đơn và trừ tồn kho dưới tên người khác, hoặc đọc đơn của họ.
    /// </para>
    /// <para>
    /// Ném nếu null là chủ ý: <c>[Authorize]</c> đã bảo đảm có danh tính, nên null ở
    /// đây là lỗi lập trình (thiếu claim, sai tên claim) và phải nổ to ngay thay vì
    /// âm thầm thao tác trên userId = 0.
    /// </para>
    /// </summary>
    private int UserId => _currentUser.Id
        ?? throw new InvalidOperationException(
            "Không đọc được danh tính dù action đã có [Authorize]. " +
            "Kiểm tra claim ClaimTypes.NameIdentifier lúc đăng nhập.");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var cart = await _cartService.GetCartAsync(cancellationToken);

        // Giỏ rỗng thì KHÔNG render trang xác nhận: một trang "xác nhận đặt hàng"
        // không có gì để xác nhận là trang vô nghĩa, và nó còn tạo ra đường dẫn tới
        // một POST đặt đơn rỗng. Đẩy về /Cart kèm lời giải thích.
        //
        // Đây cũng là trường hợp xảy ra thật, không phải giả định: giỏ có thể rỗng vì
        // toàn bộ sản phẩm trong đó đã bị xoá khỏi shop (GetCartAsync lọc dòng chết).
        if (cart.IsEmpty)
        {
            TempData["CartNotice"] = "Giỏ hàng đang trống nên chưa thể đặt hàng.";

            return RedirectToAction("Index", "Cart");
        }

        return View(new CheckoutViewModel { Cart = cart });
    }

    /// <summary>
    /// Chốt đơn. Là POST vì đây là thao tác GHI có hệ quả thật (trừ tồn kho, tạo
    /// đơn); nếu là GET thì chỉ cần nhúng một thẻ img trỏ tới URL này vào trang bất
    /// kỳ là đặt đơn hộ người khác.
    ///
    /// <para>
    /// Nhận thông tin giao hàng từ form, nhưng KHÔNG nhận <c>userId</c>. Ranh giới là:
    /// dữ liệu người dùng tự khai về đơn của họ thì phải đến từ request; DANH TÍNH thì
    /// tuyệt đối không - nhận nó từ form là mở lỗ IDOR (đặt đơn dưới tên người khác).
    /// </para>
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(
        CheckoutViewModel form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await RenderLaiFormAsync(form, cancellationToken);
        }

        try
        {
            var ketQua = await _orderService.CheckoutAsync(
                UserId,
                new ShippingInfo(form.RecipientName, form.RecipientPhone, form.ShippingAddress),
                cancellationToken);

            if (form.PhuongThuc == PhuongThucThanhToan.VnPay)
            {
                // Đơn đã tạo xong và đang ở Pending. Giờ mới dựng lệnh thanh toán -
                // THỨ TỰ này bắt buộc: vnp_TxnRef là OrderId và vnp_Amount là
                // TotalAmount đã chốt, cả hai chỉ tồn tại sau khi đơn được lưu.
                var url = await _paymentService.TaoUrlThanhToanAsync(
                    ketQua.OrderId,
                    UserId,

                    // IP THẬT của khách, VNPay dùng cho chống gian lận. "unknown" chỉ
                    // xảy ra trong test (WebApplicationFactory không có kết nối thật).
                    HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    cancellationToken);

                // Redirect chứ KHÔNG LocalRedirect: đích nằm ở tên miền khác.
                //
                // Đây là ngoại lệ có kiểm soát với quy tắc chống open-redirect: URL này
                // do SERVER dựng từ BaseUrl trong cấu hình, không có một ký tự nào đến
                // từ request. Nhận URL đích từ query string rồi Redirect mới là lỗ hổng.
                return Redirect(url);
            }

            // Post-Redirect-Get: sau khi đặt hàng thành công BẮT BUỘC redirect, nếu
            // không thì người dùng bấm F5 sẽ được hỏi gửi lại form - và lần này gửi
            // lại nghĩa là đặt thêm một đơn nữa.
            return RedirectToAction(nameof(Success), new { id = ketQua.OrderId });
        }
        catch (InsufficientStockException ex)
        {
            // ★ ĐỔI SO VỚI TRƯỚC: render lại form thay vì redirect về /Cart.
            //
            // Lý do đổi là form địa chỉ vừa xuất hiện. Redirect vứt sạch mọi thứ người
            // dùng vừa gõ, nên họ mất địa chỉ chỉ vì có người khác mua trước - một lỗi
            // hoàn toàn không phải của họ. PRG vẫn giữ nguyên cho nhánh THÀNH CÔNG
            // (nơi bấm F5 tạo đơn trùng); gửi lại một POST đã thất bại thì không tạo
            // ra đơn thứ hai nào.
            //
            // ex.Message đã nói rõ sản phẩm nào và còn bao nhiêu - dùng nguyên văn,
            // đây chính là thông báo sinh ra từ nhánh xung đột RowVersion.
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (NotFoundException)
        {
            ModelState.AddModelError(string.Empty,
                "Có sản phẩm trong giỏ không còn được bán. Vui lòng kiểm tra lại giỏ hàng.");
        }
        catch (EmptyCartException ex)
        {
            // Nhánh DUY NHẤT vẫn redirect: giỏ rỗng thì không còn gì để render trên
            // trang xác nhận, và giữ người dùng ở đây là bắt họ nhìn một cái bảng
            // trống. Index cũng chặn y hệt vì cùng lý do đó.
            TempData["CartNotice"] = ex.Message;

            return RedirectToAction("Index", "Cart");
        }

        return await RenderLaiFormAsync(form, cancellationToken);
    }

    /// <summary>
    /// Hiện lại trang xác nhận kèm lỗi, GIỮ NGUYÊN những gì người dùng đã gõ.
    ///
    /// <para>
    /// Bắt buộc nạp lại <c>form.Cart</c>: nó có <c>[BindNever]</c> nên sau model
    /// binding luôn rỗng. Quên dòng đó thì trang hiện ra với giỏ hàng TRỐNG kèm một
    /// thông báo lỗi - và không có exception nào báo, vì <c>CartView.Empty</c> là một
    /// giá trị hoàn toàn hợp lệ.
    /// </para>
    /// </summary>
    private async Task<IActionResult> RenderLaiFormAsync(
        CheckoutViewModel form,
        CancellationToken cancellationToken)
    {
        form.Cart = await _cartService.GetCartAsync(cancellationToken);

        if (form.Cart.IsEmpty)
        {
            TempData["CartNotice"] = "Giỏ hàng đang trống nên chưa thể đặt hàng.";

            return RedirectToAction("Index", "Cart");
        }

        // View(nameof(Index), ...) chứ không phải View(): action đang chạy tên là
        // Confirm nên View() sẽ đi tìm Views/Checkout/Confirm.cshtml - file không tồn
        // tại, và đây là loại lỗi build-được-nhưng-nổ-lúc-chạy giống hệt việc ghi sai
        // tên layout trong _ViewStart.
        return View(nameof(Index), form);
    }

    /// <summary>Trang cảm ơn. Đọc lại đơn từ DB thay vì nhận qua TempData.</summary>
    [HttpGet]
    public async Task<IActionResult> Success(int id, CancellationToken cancellationToken)
    {
        // GetMyOrderAsync lọc theo UserId nên đơn của người khác trả về null -> 404.
        // Không dùng TempData để chuyển dữ liệu đơn sang đây: TempData chỉ đọc được
        // một lần nên bấm F5 là trang trắng, và người dùng cũng cần bookmark được.
        var order = await _orderService.GetMyOrderAsync(id, UserId, cancellationToken);

        if (order is null)
        {
            // 404 cho cả "không tồn tại" và "của người khác" - phân biệt hai cái là
            // để lộ việc đơn số đó có tồn tại hay không.
            return NotFound();
        }

        return View(order);
    }
}
