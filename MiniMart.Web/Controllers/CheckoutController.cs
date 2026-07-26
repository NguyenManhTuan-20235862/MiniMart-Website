using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniMart.Application.Interfaces;
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
    private readonly ICurrentUser _currentUser;

    // Chỉ inject abstraction. Controller không biết giỏ nằm ở Session hay DB - với
    // trang này thì luôn là DB (vì [Authorize]), nhưng đó là kết luận của factory ở
    // Program.cs, không phải điều Controller được phép giả định.
    public CheckoutController(
        ICartService cartService,
        IOrderService orderService,
        ICurrentUser currentUser)
    {
        _cartService = cartService;
        _orderService = orderService;
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

        // ChoPhepSua: false -> bảng chỉ để ĐỌC LẠI. Cho sửa số lượng ngay tại đây sẽ
        // khiến form POST về /Cart/UpdateQuantity rồi redirect về /Cart, tức người dùng
        // bị đá khỏi luồng đặt hàng mà không hiểu vì sao.
        return View(new CartTableViewModel(cart, ChoPhepSua: false));
    }

    /// <summary>
    /// Chốt đơn. Là POST vì đây là thao tác GHI có hệ quả thật (trừ tồn kho, tạo
    /// đơn); nếu là GET thì chỉ cần nhúng một thẻ img trỏ tới URL này vào trang bất
    /// kỳ là đặt đơn hộ người khác.
    ///
    /// <para>
    /// Không nhận tham số nào từ request - kể cả <c>userId</c>. Toàn bộ đầu vào là
    /// giỏ hàng dưới DB + danh tính từ cookie, nên không có gì để giả mạo.
    /// </para>
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(CancellationToken cancellationToken)
    {
        try
        {
            var ketQua = await _orderService.CheckoutAsync(UserId, cancellationToken);

            // Post-Redirect-Get: sau khi đặt hàng thành công BẮT BUỘC redirect, nếu
            // không thì người dùng bấm F5 sẽ được hỏi gửi lại form - và lần này gửi
            // lại nghĩa là đặt thêm một đơn nữa.
            return RedirectToAction(nameof(Success), new { id = ketQua.OrderId });
        }
        // Ba nhánh dưới đều đẩy về /Cart chứ không render lại /Checkout: cả ba đều
        // cần người dùng SỬA giỏ hàng, mà trang /Checkout cố ý không sửa được.
        catch (InsufficientStockException ex)
        {
            // Thông báo đã nói rõ sản phẩm nào và còn bao nhiêu - dùng nguyên văn.
            TempData["CartNotice"] = ex.Message;
        }
        catch (NotFoundException)
        {
            TempData["CartNotice"] =
                "Có sản phẩm trong giỏ không còn được bán. Vui lòng kiểm tra lại giỏ hàng.";
        }
        catch (EmptyCartException ex)
        {
            // Index đã chặn giỏ rỗng, nên tới được đây nghĩa là giỏ vừa bị làm rỗng
            // ở tab khác hoặc sản phẩm vừa bị gỡ bán.
            TempData["CartNotice"] = ex.Message;
        }

        return RedirectToAction("Index", "Cart");
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
