using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Application.Interfaces;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

public class AuthHardeningTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private string _username = "";
    private const string MatKhauDung = "MatKhau123";

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

        _username = $"th_{Guid.NewGuid():N}"[..14];
        await userService.RegisterAsync(_username, MatKhauDung);
    }

    // ---------- Timing attack ----------

    [Fact]
    public async Task Username_khong_ton_tai_khong_duoc_tra_loi_nhanh_hon_dang_ke()
    {
        using var scope = _factory.Services.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

        // Làm nóng: lần gọi đầu phải JIT và tính hash giả (static Lazy), nếu đo
        // luôn thì con số đầu tiên bị đội lên và test đỏ oan.
        await userService.AuthenticateAsync(_username, "sai-mat-khau");
        await userService.AuthenticateAsync("khong-ton-tai-gi-ca", "sai-mat-khau");

        var thoiGianUserCoThat = await DoThoiGianAsync(
            () => userService.AuthenticateAsync(_username, "sai-mat-khau"));

        var thoiGianUserKhongCo = await DoThoiGianAsync(
            () => userService.AuthenticateAsync("khong-ton-tai-gi-ca", "sai-mat-khau"));

        // Không đòi hai con số bằng nhau - đo thời gian luôn nhiễu. Điều cần
        // chặn là chênh lệch BẬC ĐỘ LỚN: nếu bỏ verify hash giả thì username sai
        // trả lời trong ~1ms còn username đúng ~100ms, tỉ lệ vài chục lần.
        var tiLe = thoiGianUserCoThat / Math.Max(thoiGianUserKhongCo, 0.01);

        Assert.InRange(tiLe, 0.1, 10.0);
    }

    private static async Task<double> DoThoiGianAsync(Func<Task> hanhDong)
    {
        // Lấy trung vị của 5 lần để bớt nhiễu từ GC và scheduler.
        var soDo = new List<double>();

        for (var i = 0; i < 5; i++)
        {
            var dongHo = Stopwatch.StartNew();
            await hanhDong();
            dongHo.Stop();
            soDo.Add(dongHo.Elapsed.TotalMilliseconds);
        }

        soDo.Sort();
        return soDo[2];
    }

    [Fact]
    public async Task Username_khong_ton_tai_van_tra_ve_null()
    {
        using var scope = _factory.Services.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

        // Thêm verify hash giả không được làm sai kết quả.
        Assert.Null(await userService.AuthenticateAsync("khong-ton-tai-gi-ca", MatKhauDung));
        Assert.Null(await userService.AuthenticateAsync(_username, "sai-mat-khau"));
        Assert.NotNull(await userService.AuthenticateAsync(_username, MatKhauDung));
    }

    // ---------- Độ mạnh mật khẩu ----------

    [Theory]
    [InlineData("abc123", "ít nhất 8 ký tự")]      // quá ngắn
    [InlineData("matkhaudai", "cả chữ và số")]     // đủ dài, thiếu số
    [InlineData("12345678", "cả chữ và số")]       // đủ dài, thiếu chữ
    public async Task Mat_khau_yeu_bi_tu_choi_kem_thong_bao_dung_cho(
        string matKhauYeu, string thongBaoMongDoi)
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var html = await DangKyAsync(client, $"y_{Guid.NewGuid():N}"[..12], matKhauYeu);

        Assert.Contains(thongBaoMongDoi, html);
    }

    [Fact]
    public async Task Mat_khau_du_manh_thi_dang_ky_thanh_cong()
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await PostDangKyAsync(client, $"m_{Guid.NewGuid():N}"[..12], "MatKhau2026");

        // Thành công thì Post-Redirect-Get, không render lại form.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<string> DangKyAsync(HttpClient client, string username, string password)
    {
        var response = await PostDangKyAsync(client, username, password);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> PostDangKyAsync(
        HttpClient client, string username, string password)
    {
        var token = await LayAntiForgeryTokenAsync(client, "/Account/Register");

        return await client.PostAsync("/Account/Register", new FormUrlEncodedContent(
        [
            new("Username", username),
            new("Password", password),
            new("ConfirmPassword", password),
            new("__RequestVerificationToken", token)
        ]));
    }

    private static async Task<string> LayAntiForgeryTokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        const string moc = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var batDau = html.IndexOf(moc, StringComparison.Ordinal) + moc.Length;

        return html[batDau..html.IndexOf('"', batDau)];
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Users
            .Where(u => u.Username.StartsWith("th_")
                     || u.Username.StartsWith("y_")
                     || u.Username.StartsWith("m_"))
            .ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
