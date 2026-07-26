using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Web.Models;

namespace MiniMart.Web.Controllers;

public class AccountController : Controller
{
    private readonly IUserService _userService;
    private readonly ICartService _cartService;
    private readonly ICartStore _gioKhach;
    private readonly ICartStore _gioThanhVien;

    // Chỉ inject abstraction: IUserService / ICartService (Application) và
    // ICartStore (Domain). Controller không hề biết UserService, SessionCartStore,
    // DatabaseCartStore hay DbContext tồn tại - hai kho được phân biệt bằng KHOÁ,
    // không bằng tên class.
    public AccountController(
        IUserService userService,
        ICartService cartService,
        [FromKeyedServices(CartStoreKeys.Session)] ICartStore gioKhach,
        [FromKeyedServices(CartStoreKeys.Database)] ICartStore gioThanhVien)
    {
        _userService = userService;
        _cartService = cartService;
        _gioKhach = gioKhach;
        _gioThanhVien = gioThanhVien;
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        RegisterViewModel model,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var user = await _userService.RegisterAsync(model.Username, model.Password);

            // Đăng ký xong đăng nhập luôn, khỏi bắt người dùng nhập lại.
            await SignInUserAsync(user, isPersistent: false, cancellationToken);

            return RedirectToLocal(returnUrl);
        }
        catch (UsernameAlreadyExistsException ex)
        {
            // Gắn lỗi vào đúng ô Username để hiện ngay dưới ô đó.
            ModelState.AddModelError(nameof(model.Username), ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    // Rate limit chỉ đặt trên POST, không đặt trên GET: xem trang đăng nhập là
    // hành vi bình thường, chỉ việc THỬ mật khẩu mới cần giới hạn.
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userService.AuthenticateAsync(model.Username, model.Password);

        if (user is null)
        {
            // Thông báo CHUNG, không tiết lộ sai username hay sai mật khẩu.
            ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc mật khẩu không đúng.");
            return View(model);
        }

        await SignInUserAsync(user, model.RememberMe, cancellationToken);

        return RedirectToLocal(returnUrl);
    }

    // POST chứ không GET: nếu Logout là GET thì chỉ cần nhúng <img src="/Account/Logout">
    // vào một trang bất kỳ là đá được người khác ra khỏi phiên.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    /// <summary>
    /// Biến một User nghiệp vụ thành danh tính HTTP, cấp cookie, rồi gộp giỏ hàng.
    /// Việc này thuộc về Web: nó cần HttpContext, thứ mà tầng Application
    /// không được phép biết đến.
    ///
    /// <para>
    /// Gộp giỏ nằm Ở ĐÂY chứ không ở trong từng action: cả <c>Login</c> và
    /// <c>Register</c> đều đi qua method này, nên không thể thêm đường đăng nhập
    /// thứ ba mà quên gộp. Đặt ở mỗi action là hai chỗ để quên.
    /// </para>
    /// </summary>
    private async Task SignInUserAsync(User user, bool isPersistent, CancellationToken cancellationToken)
    {
        var claims = new List<Claim>
        {
            // Khoá định danh - đọc lại bằng User.FindFirstValue(ClaimTypes.NameIdentifier)
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),

            // Đổ vào User.Identity.Name
            new(ClaimTypes.Name, user.Username),

            // BẮT BUỘC đúng ClaimTypes.Role thì [Authorize(Roles = "Admin")]
            // và User.IsInRole("Admin") mới hoạt động.
            new(ClaimTypes.Role, user.Role.ToString())
        };

        // Tham số thứ hai (authenticationType) là thứ quyết định
        // IsAuthenticated = true. Bỏ trống thì principal có đủ claims nhưng
        // vẫn bị coi là CHƯA đăng nhập.
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                // true  -> cookie có hạn, sống qua lần đóng trình duyệt
                // false -> session cookie, mất khi đóng trình duyệt
                IsPersistent = isPersistent
            });

        // ★ SignInAsync chỉ GHI COOKIE VÀO RESPONSE. Nó KHÔNG cập nhật
        // HttpContext.User của request đang chạy - User chỉ được UseAuthentication
        // gán, mà middleware đó đã chạy xong từ đầu request này.
        //
        // Không có dòng dưới đây thì ICurrentUser.Id vẫn null, và DatabaseCartStore
        // ném InvalidOperationException ngay khi gộp. Gán tay là diễn đạt đúng sự
        // thật: kể từ giây phút này request ĐÃ có danh tính.
        HttpContext.User = principal;

        await GopGioHangAsync(cancellationToken);
    }

    /// <summary>
    /// Chuyển giỏ khách vãng lai (Session) vào giỏ thành viên (DB) rồi xoá giỏ cũ.
    ///
    /// <para>
    /// Truyền hai kho TƯỜNG MINH bằng Keyed DI thay vì để factory chọn: gộp là
    /// thao tác trên cả hai kho cùng lúc, một <c>ICartStore</c> duy nhất không diễn
    /// đạt được. Đây là chỗ DUY NHẤT trong dự án được phép bỏ qua factory.
    /// </para>
    /// </summary>
    private async Task GopGioHangAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cartService.MergeAsync(_gioKhach, _gioThanhVien, cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            // Hai request đồng thời cùng tạo giỏ lần đầu -> UNIQUE(Carts.UserId)
            // chặn cái thứ hai.
            //
            // Bỏ qua có chủ đích, và CHỈ exception này: cookie đăng nhập đã được
            // ghi vào response ở trên, nên ném ra đây là biến một lần đăng nhập
            // THÀNH CÔNG thành trang lỗi 500. Mất giỏ vãng lai thì lần đăng nhập
            // sau gộp lại được (MergeAsync xoá giỏ nguồn SAU khi lưu giỏ đích);
            // chặn đăng nhập thì không cứu được gì. Lỗi lập trình thật vẫn nổ to.
        }
    }

    /// <summary>
    /// Chỉ redirect tới đường dẫn nội bộ. Nhận thẳng returnUrl từ query string
    /// sẽ tạo lỗ hổng open redirect: /Account/Login?returnUrl=https://site-lua-dao
    /// </summary>
    private IActionResult RedirectToLocal(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl!)
            : RedirectToAction("Index", "Home");
}
