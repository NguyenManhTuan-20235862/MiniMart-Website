using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Integration test cho Controller trong Area Admin: chạy qua pipeline HTTP
/// thật, gồm cả xác thực, phân quyền, antiforgery và model binding.
/// </summary>
public class AdminCrudTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernames = [];
    private readonly List<string> _categoryNames = [];

    public Task InitializeAsync() => Task.CompletedTask;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>
    /// Đăng ký tài khoản qua HTTP, nâng quyền Admin dưới DB, rồi đăng nhập LẠI.
    /// Bước đăng nhập lại là bắt buộc: claims trong cookie là ảnh chụp tại thời
    /// điểm đăng nhập, đổi Role dưới DB không làm cookie cũ tự cập nhật.
    /// </summary>
    private async Task<HttpClient> TaoClientAdminAsync()
    {
        var client = CreateClient();
        var username = $"adm_{Guid.NewGuid():N}"[..16];
        const string password = "MatKhau123";

        _usernames.Add(username);
        await PostFormAsync(client, "/Account/Register", new()
        {
            ["Username"] = username,
            ["Password"] = password,
            ["ConfirmPassword"] = password
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            var user = await context.Users.SingleAsync(u => u.Username == username);
            user.Role = UserRole.Admin;
            await context.SaveChangesAsync();
        }

        await PostFormAsync(client, "/Account/Login", new()
        {
            ["Username"] = username,
            ["Password"] = password,
            ["RememberMe"] = "false"
        });

        return client;
    }

    // ───────────── Phân quyền ─────────────

    [Theory]
    [InlineData("/Admin/Category")]
    [InlineData("/Admin/Product")]
    public async Task Nac_danh_khong_vao_duoc_trang_quan_tri(string path)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Admin_vao_duoc_trang_quan_ly_danh_muc()
    {
        using var client = await TaoClientAdminAsync();

        var response = await client.GetAsync("/Admin/Category");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ───────────── Layout ─────────────

    [Theory]
    [InlineData("/Admin/Dashboard")]
    [InlineData("/Admin/Category")]
    [InlineData("/Admin/Product")]
    public async Task Trang_quan_tri_phai_dung_layout_rieng(string path)
    {
        using var client = await TaoClientAdminAsync();

        var html = await client.GetStringAsync(path);

        // Layout được phân giải lúc CHẠY, không phải lúc biên dịch - đổi sai
        // tên trong _ViewStart thì build vẫn qua, chỉ nổ khi mở trang.
        Assert.Contains("MiniMart Quản trị", html);
        Assert.Contains("Về trang khách hàng", html);
    }

    [Fact]
    public async Task Trang_khach_hang_khong_duoc_dung_layout_quan_tri()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("MiniMart Quản trị", html);
    }

    // ───────────── Quy tắc nghiệp vụ qua HTTP ─────────────

    [Fact]
    public async Task Xoa_danh_muc_con_san_pham_bi_chan_va_du_lieu_khong_mat()
    {
        using var client = await TaoClientAdminAsync();
        var categoryName = $"DM_{Guid.NewGuid():N}"[..16];
        _categoryNames.Add(categoryName);

        await PostFormAsync(client, "/Admin/Category/Create", new() { ["Name"] = categoryName });

        var categoryId = await LayCategoryIdAsync(categoryName);
        await PostFormAsync(client, "/Admin/Product/Create", new()
        {
            ["Name"] = "San pham test",
            ["Price"] = "100000",
            ["Stock"] = "3",
            ["CategoryId"] = categoryId.ToString()
        });

        var response = await PostFormAsync(client, $"/Admin/Category/Delete/{categoryId}", new()
        {
            ["id"] = categoryId.ToString()
        });

        // Phải là redirect kèm thông báo, KHÔNG phải 500 do exception lọt ra.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
        Assert.True(await context.Categories.AnyAsync(c => c.Id == categoryId));
    }

    [Fact]
    public async Task Tao_san_pham_thieu_du_lieu_thi_dropdown_danh_muc_phai_duoc_nap_lai()
    {
        using var client = await TaoClientAdminAsync();
        var categoryName = $"DM_{Guid.NewGuid():N}"[..16];
        _categoryNames.Add(categoryName);

        await PostFormAsync(client, "/Admin/Category/Create", new() { ["Name"] = categoryName });

        // Thiếu Name -> ModelState không hợp lệ -> render lại form.
        var response = await PostFormAsync(client, "/Admin/Product/Create", new()
        {
            ["Name"] = "",
            ["Price"] = "100000",
            ["Stock"] = "3",
            ["CategoryId"] = "1"
        });

        var html = await response.Content.ReadAsStringAsync();

        // Lỗi kinh điển của MVC: quên nạp lại dropdown khi render lại form,
        // khiến người dùng mất luôn danh sách lựa chọn sau một lỗi nhập liệu.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(categoryName, html);
    }

    // ───────────── Helper ─────────────

    private async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string path,
        Dictionary<string, string> fields)
    {
        var form = await client.GetStringAsync(path);
        fields["__RequestVerificationToken"] = LayAntiForgeryToken(form);

        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static string LayAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            """name="__RequestVerificationToken"[^>]*value="([^"]+)""");

        Assert.True(match.Success, "Không tìm thấy antiforgery token trong form.");
        return match.Groups[1].Value;
    }

    private async Task<int> LayCategoryIdAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        return (await context.Categories.SingleAsync(c => c.Name == name)).Id;
    }

    public async Task DisposeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

            await context.Products
                .Where(p => _categoryNames.Contains(p.Category.Name))
                .ExecuteDeleteAsync();

            await context.Categories
                .Where(c => _categoryNames.Contains(c.Name))
                .ExecuteDeleteAsync();

            await context.Users
                .Where(u => _usernames.Contains(u.Username))
                .ExecuteDeleteAsync();
        }

        _factory.Dispose();
    }
}
