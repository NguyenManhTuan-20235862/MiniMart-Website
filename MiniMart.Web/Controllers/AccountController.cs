using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Web.Models;

namespace MiniMart.Web.Controllers;

public class AccountController : Controller
{
    private readonly IUserService _userService;

    // Chỉ inject IUserService (Application). Controller không hề biết
    // UserService, UserRepository hay DbContext tồn tại.
    public AccountController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var user = await _userService.RegisterAsync(model.Username, model.Password);

            // Đăng ký xong đăng nhập luôn, khỏi bắt người dùng nhập lại.
            await SignInUserAsync(user, isPersistent: false);

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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
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

        await SignInUserAsync(user, model.RememberMe);

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
    /// Biến một User nghiệp vụ thành danh tính HTTP rồi cấp cookie.
    /// Việc này thuộc về Web: nó cần HttpContext, thứ mà tầng Application
    /// không được phép biết đến.
    /// </summary>
    private async Task SignInUserAsync(User user, bool isPersistent)
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
