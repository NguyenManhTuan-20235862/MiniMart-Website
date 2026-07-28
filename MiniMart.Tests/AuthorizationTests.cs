using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Kiểm chứng AuthorizationMiddleware trên pipeline thật.
///
/// Nhóm test "nặc danh" KHÔNG cần database: request bị chặn trước khi tới
/// controller nên không có truy vấn nào chạy. Nhóm test theo vai trò cần DB
/// vì phải đăng ký tài khoản thật; các tài khoản đó được dọn ở DisposeAsync.
/// </summary>
public class AuthorizationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernamesToCleanUp = [];

    public Task InitializeAsync() => Task.CompletedTask;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // Phải tắt auto-redirect, nếu không HttpClient tự đi theo 302 và
            // ta mất luôn thông tin cần kiểm tra.
            AllowAutoRedirect = false
        });

    // ───────────── Nhóm 1: chưa đăng nhập ─────────────

    [Theory]
    [InlineData("/Profile")]
    [InlineData("/Admin/Dashboard")]
    public async Task Chua_dang_nhap_thi_bi_day_ve_trang_Login(string path)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(path);

        // Nhánh Challenge: chưa xác thực được -> LoginPath.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", DuongDanRedirect(response));
    }

    [Fact]
    public async Task Redirect_ve_Login_phai_giu_lai_ReturnUrl()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/Profile");

        // Mất ReturnUrl thì đăng nhập xong người dùng bị vứt về trang chủ.
        Assert.Contains("ReturnUrl", DuongDanRedirect(response));
    }

    [Fact]
    public async Task Trang_cong_khai_khong_bi_chan()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/");

        // Đối chứng: chứng minh middleware chặn CÓ CHỌN LỌC theo metadata,
        // chứ không phải chặn tất cả.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ───────────── Nhóm 2: đã đăng nhập, sai vai trò ─────────────

    [Fact]
    public async Task Customer_vao_khu_vuc_Admin_thi_bi_day_ve_AccessDenied()
    {
        using var client = CreateClient();
        var username = $"test_{Guid.NewGuid():N}"[..20];
        await DangKyAsync(client, username, "MatKhau123");

        var response = await client.GetAsync("/Admin/Dashboard");

        // Nhánh Forbid: ĐÃ xác thực nhưng thiếu quyền -> AccessDeniedPath.
        // Đây là khác biệt cốt lõi giữa Challenge và Forbid.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", DuongDanRedirect(response));
    }

    [Fact]
    public async Task Customer_van_vao_duoc_trang_chi_yeu_cau_dang_nhap()
    {
        using var client = CreateClient();
        var username = $"test_{Guid.NewGuid():N}"[..20];
        await DangKyAsync(client, username, "MatKhau123");

        var response = await client.GetAsync("/Profile");

        // [Authorize] trần chỉ đòi đăng nhập, không đòi vai trò.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ───────────── Helper ─────────────

    /// <summary>
    /// Location header trả về URI tuyệt đối (http://localhost/...), nên phải
    /// lấy phần path+query mới so sánh được với đường dẫn cấu hình.
    /// </summary>
    private static string DuongDanRedirect(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        Assert.NotNull(location);

        return location!.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    private async Task DangKyAsync(HttpClient client, string username, string password)
    {
        _usernamesToCleanUp.Add(username);

        await client.DangKyAsync(username, password);
    }

    public async Task DisposeAsync()
    {
        if (_usernamesToCleanUp.Count > 0)
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

            await context.Users
                .Where(u => _usernamesToCleanUp.Contains(u.Username))
                .ExecuteDeleteAsync();
        }

        _factory.Dispose();
    }
}
