using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MiniMart.Web.Controllers;

/// <summary>
/// [Authorize] trần: chỉ cần đăng nhập, không phân biệt vai trò.
/// </summary>
[Authorize]
public class ProfileController : Controller
{
    public IActionResult Index() => View();
}
