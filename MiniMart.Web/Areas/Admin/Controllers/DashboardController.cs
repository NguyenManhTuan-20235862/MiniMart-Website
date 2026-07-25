using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MiniMart.Web.Areas.Admin.Controllers;

/// <summary>
/// Khu vực quản trị: URL dạng /Admin/Dashboard.
/// Đặt [Authorize] ở cấp CLASS nên mọi action trong đây đều được bảo vệ - thêm
/// action mới sẽ tự động an toàn. Đặt ở từng action thì chỉ cần quên một lần
/// là hở một lỗ.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
public class DashboardController : Controller
{
    public IActionResult Index() => View();
}
