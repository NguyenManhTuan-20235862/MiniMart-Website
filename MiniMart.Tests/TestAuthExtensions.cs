using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Đăng ký / đăng nhập dùng chung cho các bộ test cần một client đã xác thực.
///
/// <para>
/// Gộp lại vì ngưỡng của dự án là <b>bản copy thứ ba</b> và việc này đã có tới chín
/// bản. Nhưng lý do thật sự không phải số dòng lặp — mà là <b>chín bản đã lệch nhau</b>,
/// và ba trong số đó lệch theo hướng nguy hiểm: chúng chấp nhận <c>200</c> là đăng nhập
/// thành công. Xem <see cref="DangNhapAsync"/>.
/// </para>
/// <para>
/// ⚠ <b>Ngoại lệ có chủ đích: <c>AuthHardeningTests</c> KHÔNG dùng file này.</b> Nó là
/// bộ test của chính cơ chế đăng nhập, nên nếu nó gọi helper ở đây thì nó đang dùng thứ
/// đang được kiểm để dựng dữ liệu đầu vào cho phép kiểm — và sẽ xanh kể cả khi cả hai
/// cùng sai. Cùng lý do mà hàm ký trong test VNPay được viết lại thay vì gọi
/// <c>VnPayService</c> (xem <c>rules/payments.md</c>).
/// </para>
/// </summary>
internal static class TestAuthExtensions
{
    public const string MatKhauMacDinh = "MatKhau123";

    /// <summary>
    /// Sinh username duy nhất, đủ ngắn để lọt giới hạn độ dài của cột.
    /// </summary>
    public static string SinhUsername(string tienTo) => $"{tienTo}_{Guid.NewGuid():N}"[..16];

    /// <summary>
    /// Đăng ký và KHẲNG ĐỊNH thành công.
    ///
    /// <para>
    /// Đăng ký hỏng cũng trả <c>200</c> (render lại form kèm lỗi), nên bỏ qua bước
    /// khẳng định là để test đỏ ở một assertion nói về chuyện hoàn toàn khác.
    /// </para>
    /// </summary>
    public static async Task DangKyAsync(
        this HttpClient client,
        string username,
        string password = MatKhauMacDinh)
    {
        var response = await client.PostFormAsync("/Account/Register", new()
        {
            ["Username"] = username,
            ["Password"] = password,
            ["ConfirmPassword"] = password
        });

        BaoDamRoiKhoiForm(response, "/Account/Register", $"Đăng ký '{username}'");
    }

    /// <summary>
    /// Đăng nhập và KHẲNG ĐỊNH thành công.
    ///
    /// <para>
    /// ★ Đây là lý do quan trọng nhất khiến chín bản helper phải gộp lại. Ba bản cũ
    /// chấp nhận <c>Found or OK</c> vô điều kiện, mà với client KHÔNG đi theo redirect
    /// thì <c>200</c> chính là đăng nhập <b>THẤT BẠI</b>: chỉ nhánh thành công của
    /// <c>AccountController.Login</c> mới <c>RedirectToLocal</c>, mọi nhánh thất bại
    /// đều là <c>View()</c>. Helper cho qua → client không có cookie → request tới
    /// <c>/Admin/...</c> bị đá về trang đăng nhập → test đỏ ở một assertion nói về HTML
    /// của bảng, không manh mối nào chỉ về đăng nhập.
    /// </para>
    /// <para>
    /// Cách phân biệt đúng nằm ở <see cref="BaoDamRoiKhoiForm"/> — <b>không</b> phải
    /// "đòi đúng 302", vì điều đó chỉ đúng cho một trong hai kiểu client.
    /// </para>
    /// </summary>
    public static async Task DangNhapAsync(
        this HttpClient client,
        string username,
        string password = MatKhauMacDinh)
    {
        var response = await client.PostFormAsync("/Account/Login", new()
        {
            ["Username"] = username,
            ["Password"] = password,
            ["RememberMe"] = "false"
        });

        BaoDamRoiKhoiForm(response, "/Account/Login", $"Đăng nhập '{username}'");
    }

    /// <summary>
    /// Khẳng định form đã được chấp nhận, đúng cho <b>cả hai</b> kiểu client.
    ///
    /// <para>
    /// ★ Đây là chỗ mà mọi bản helper cũ đều làm sai theo một trong hai hướng, và là
    /// lý do chúng không thể gộp bằng cách chép một bản đè lên tám bản kia:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <c>AllowAutoRedirect = false</c> → thành công là <b>302</b>, thất bại là 200.
    /// </item>
    /// <item>
    /// <c>AllowAutoRedirect = true</c> → HttpClient tự đi theo redirect, nên thành công
    /// <b>cũng là 200</b>. Đòi đúng 302 ở đây làm 21 test đỏ — đã gặp thật khi gộp.
    /// </item>
    /// </list>
    /// <para>
    /// Nên tín hiệu đúng không phải mã trạng thái mà là <b>đã rời khỏi trang form hay
    /// chưa</b>: mọi nhánh thất bại của <c>AccountController</c> đều <c>View()</c> lại
    /// chính URL đó. <c>RequestMessage.RequestUri</c> là URI CUỐI CÙNG sau khi đã đi
    /// hết chuỗi redirect, nên nó trả lời được câu hỏi này bất kể client cấu hình sao.
    /// </para>
    /// </summary>
    private static void BaoDamRoiKhoiForm(
        HttpResponseMessage response,
        string duongDanForm,
        string moTa)
    {
        var duongDanCuoi = response.RequestMessage?.RequestUri?.AbsolutePath ?? duongDanForm;

        var thanhCong =
            response.StatusCode is HttpStatusCode.Found
            || (response.IsSuccessStatusCode
                && !duongDanCuoi.Equals(duongDanForm, StringComparison.OrdinalIgnoreCase));

        Assert.True(
            thanhCong,
            $"{moTa} không thành công: nhận {(int)response.StatusCode}, dừng ở '{duongDanCuoi}'. "
            + "200 ngay trên trang form = sai thông tin hoặc ModelState hỏng; "
            + "429 = vượt RateLimiting:LoginPermitLimit của factory này.");
    }

    public static Task<HttpResponseMessage> DangXuatAsync(this HttpClient client) =>
        client.PostFormAsync("/Account/Logout", []);

    /// <summary>
    /// Client mới, đã đăng ký, đã đăng nhập, quyền <c>Customer</c>.
    /// </summary>
    /// <returns>Client và username vừa tạo — người gọi tự thêm username vào danh sách dọn dẹp.</returns>
    public static async Task<(HttpClient Client, string Username)> TaoClientKhachAsync(
        this WebApplicationFactory<Program> factory,
        string tienTo)
    {
        var client = factory.TaoClient();
        var username = SinhUsername(tienTo);

        // Đăng ký đã tự đăng nhập luôn (SignInUserAsync), nên không gọi DangNhapAsync.
        await client.DangKyAsync(username);

        return (client, username);
    }

    /// <summary>
    /// Client mới, đã đăng nhập, quyền <c>Admin</c>.
    ///
    /// <para>
    /// Phải đăng nhập LẠI sau khi nâng quyền: claims là <b>ảnh chụp lúc đăng nhập</b>,
    /// nên đổi <c>Role</c> dưới DB không có hiệu lực cho phiên đang mở. Bỏ bước này thì
    /// mọi request tới <c>/Admin</c> bị 403 dù DB đã ghi đúng quyền.
    /// </para>
    /// </summary>
    public static async Task<(HttpClient Client, string Username)> TaoClientAdminAsync(
        this WebApplicationFactory<Program> factory,
        string tienTo)
    {
        var client = factory.TaoClient();
        var username = SinhUsername(tienTo);

        await client.DangKyAsync(username);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            var user = await context.Users.SingleAsync(u => u.Username == username);
            user.Role = UserRole.Admin;
            await context.SaveChangesAsync();
        }

        await client.DangNhapAsync(username);

        return (client, username);
    }

    /// <summary>
    /// Client KHÔNG tự đi theo redirect — mặc định của mọi bộ test ở đây.
    ///
    /// <para>
    /// Đi theo redirect làm mất chính thông tin cần khẳng định: "302 hay 200" là cách
    /// duy nhất phân biệt đăng nhập thành công với thất bại.
    /// </para>
    /// </summary>
    public static HttpClient TaoClient(
        this WebApplicationFactory<Program> factory,
        bool theoRedirect = false) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = theoRedirect });
}
