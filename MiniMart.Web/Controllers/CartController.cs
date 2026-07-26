using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using MiniMart.Application.Interfaces;
using MiniMart.Application.Models;
using MiniMart.Common.Exceptions;
using MiniMart.Web.Models;

namespace MiniMart.Web.Controllers;

/// <summary>
/// Giỏ hàng. KHÔNG có [Authorize]: khách vãng lai cũng phải mua được hàng - đó
/// chính là lý do tồn tại của SessionCartStore.
///
/// <para>
/// Controller không hề biết giỏ nằm ở Session hay DB. Nó chỉ gọi ICartService,
/// còn việc chọn kho đã xảy ra một lần trong factory ở Program.cs.
/// </para>
/// </summary>
public class CartController : Controller
{
    /// <summary>Tổng số món trong giỏ, để client cập nhật badge mà không cần gọi thêm.</summary>
    public const string CartCountHeader = "X-Cart-Count";

    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    // ───────────── Đọc ─────────────

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var cart = await _cartService.GetCartAsync(cancellationToken);

        // TempData mang thông báo từ lần POST trước (đường không-JavaScript).
        return View(new CartTableViewModel(cart, TempData["CartNotice"] as string));
    }

    /// <summary>
    /// JSON cho badge trên navbar. Ngoại lệ có chủ đích với quy ước "AJAX trả
    /// PartialView": client cần một con số, không cần một khối HTML.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var cart = await _cartService.GetCartAsync(cancellationToken);

        return Json(CartSummaryDto.From(cart));
    }

    /// <summary>
    /// HTML của dropdown giỏ hàng trên navbar.
    ///
    /// <para>
    /// Đây là lý do các endpoint ghi được phép trả JSON: việc DỰNG MARKUP vẫn nằm trọn
    /// trong Razor (<c>_CartDropdown.cshtml</c>), client chỉ gọi tới đây khi cần dựng
    /// lại cả danh sách - lúc mở dropdown, hoặc sau khi giỏ đổi cấu trúc (thêm dòng
    /// mới, giỏ trở thành rỗng). JavaScript không bao giờ tạo thẻ HTML nào.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Dropdown(CancellationToken cancellationToken)
    {
        var cart = await _cartService.GetCartAsync(cancellationToken);

        return PartialView("_CartDropdown", new CartTableViewModel(cart));
    }

    // ───────────── Ghi ─────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Add(AddToCartRequest request, CancellationToken cancellationToken) =>
        ThucHienAsync(
            () => _cartService.AddAsync(request.ProductId, request.Quantity, cancellationToken),
            request.ProductId,
            cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateQuantity(
        UpdateCartQuantityRequest request,
        CancellationToken cancellationToken) =>
        ThucHienAsync(
            () => _cartService.UpdateQuantityAsync(request.ProductId, request.Quantity, cancellationToken),
            request.ProductId,
            cancellationToken);

    /// <remarks>
    /// POST chứ không GET, dù chỉ là "xoá một dòng": nếu là GET thì chỉ cần nhúng
    /// &lt;img src="/Cart/Remove?productId=5"&gt; vào một trang bất kỳ là xoá được
    /// giỏ của người khác.
    /// </remarks>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Remove(
        RemoveFromCartRequest request,
        CancellationToken cancellationToken) =>
        ThucHienAsync(
            () => _cartService.RemoveAsync(request.ProductId, cancellationToken),
            request.ProductId,
            cancellationToken);

    // ───────────── Hạ tầng dùng chung ─────────────

    /// <summary>
    /// Chạy một thao tác ghi rồi trả kết quả theo ĐÚNG loại request.
    ///
    /// <para>
    /// Gom vào một chỗ vì cả ba endpoint có cùng bốn việc: kiểm ModelState, bắt
    /// exception nghiệp vụ, gắn header đếm, chọn kiểu response. Viết ba lần là ba
    /// chỗ để quên một trong bốn.
    /// </para>
    /// </summary>
    private async Task<IActionResult> ThucHienAsync(
        Func<Task<CartMutationResult>> thaoTac,
        int productId,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var loi = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault() ?? "Yêu cầu không hợp lệ.";

            return await TraKetQuaAsync(loi, productId, cancellationToken);
        }

        try
        {
            var ketQua = await thaoTac();

            return TraKetQua(ketQua.Cart, productId, ketQua.Notice);
        }
        // Ba nhánh dưới đây đều là KẾT CỤC NGHIỆP VỤ bình thường, không phải lỗi
        // hệ thống: người dùng mở trang từ lâu rồi mới bấm, trong lúc đó shop đã
        // đổi tồn kho hoặc xoá sản phẩm. Trả 500 hay 404 ở đây là biến việc bình
        // thường thành sự cố; đúng cách là trả về giỏ hàng kèm lời giải thích.
        catch (OutOfStockException ex)
        {
            return await TraKetQuaAsync(ex.Message, productId, cancellationToken);
        }
        catch (NotFoundException)
        {
            return await TraKetQuaAsync(
                "Sản phẩm không còn tồn tại và đã được bỏ khỏi danh sách.",
                productId,
                cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            // Hai request đồng thời của cùng một người cùng tạo giỏ lần đầu ->
            // UNIQUE(Carts.UserId) chặn cái thứ hai. Thử lại sạch sẽ đòi một
            // DbContext mới nên không làm được trong cùng request; bảo người dùng
            // bấm lại là đủ, vì lúc đó giỏ đã tồn tại.
            return await TraKetQuaAsync("Vui lòng thử lại.", productId, cancellationToken);
        }
    }

    private async Task<IActionResult> TraKetQuaAsync(
        string notice,
        int productId,
        CancellationToken cancellationToken)
    {
        var cart = await _cartService.GetCartAsync(cancellationToken);

        return TraKetQua(cart, productId, notice);
    }

    /// <summary>
    /// BA đường trả kết quả cho cùng một thao tác, phân biệt bằng header của request:
    ///
    /// <para>
    /// - <c>Accept: application/json</c> → <c>Json</c>. Dùng bởi dropdown giỏ hàng trên
    ///   navbar: nó chỉ cần đổi vài con số tại chỗ, thay cả khối HTML sẽ làm dropdown
    ///   nhấp nháy và mất vị trí cuộn. Xem <see cref="CartMutationDto"/> để biết hai
    ///   ràng buộc giữ cho nhánh này không vi phạm tinh thần quy ước.
    /// </para>
    /// <para>
    /// - <c>X-Requested-With: XMLHttpRequest</c> → <c>PartialView</c>, vì trình duyệt
    ///   đã có sẵn trang, chỉ cần thay phần bảng.
    /// </para>
    /// <para>
    /// - Form POST thường → <c>RedirectToAction</c> theo Post-Redirect-Get, thông
    ///   báo đi qua TempData. Không có nhánh này thì người dùng không bật
    ///   JavaScript sẽ nhận về một mảnh HTML rời làm cả trang, và bấm F5 sẽ hỏi
    ///   gửi lại form.
    /// </para>
    /// <para>
    /// Thứ tự kiểm tra quan trọng: <c>fetch</c> của dropdown gửi CẢ HAI header, nên
    /// nhánh JSON phải được xét TRƯỚC. Đảo lại thì dropdown nhận về HTML và JS đọc
    /// <c>response.json()</c> sẽ ném.
    /// </para>
    /// </summary>
    private IActionResult TraKetQua(CartView cart, int productId, string? notice)
    {
        // Header chỉ chứa chữ số ASCII nên an toàn. Thông báo tiếng Việt thì KHÔNG
        // đi qua header được (giá trị header phải là ASCII/latin1) - nó nằm trong
        // thân response, xem CartTableViewModel và CartMutationDto.
        Response.Headers[CartCountHeader] =
            cart.TotalQuantity.ToString(CultureInfo.InvariantCulture);

        if (MuonJson())
        {
            return Json(CartMutationDto.From(cart, productId, notice));
        }

        if (!LaRequestAjax())
        {
            TempData["CartNotice"] = notice;
            return RedirectToAction(nameof(Index));
        }

        return PartialView("_CartTable", new CartTableViewModel(cart, notice));
    }

    private bool LaRequestAjax() =>
        Request.Headers.XRequestedWith == "XMLHttpRequest";

    /// <remarks>
    /// Không dùng <c>Request.Headers.Accept == "application/json"</c>: trình duyệt gửi
    /// cả danh sách có q-value nên so bằng sẽ trượt. Cũng không dùng content negotiation
    /// tự động của MVC - ở đây hai nhánh trả về HAI kiểu dữ liệu khác nhau
    /// (<c>CartMutationDto</c> và <c>_CartTable</c>), không phải cùng một object hai
    /// định dạng, nên phải tự rẽ.
    /// </remarks>
    private bool MuonJson() =>
        Request.Headers.Accept.Any(giaTri =>
            giaTri is not null &&
            giaTri.Contains("application/json", StringComparison.OrdinalIgnoreCase));
}
