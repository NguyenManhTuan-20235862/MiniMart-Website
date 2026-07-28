using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MiniMart.Tests;

/// <summary>
/// Rate limit đăng nhập. Tách thành class riêng vì cần GHI ĐÈ cấu hình: giới hạn
/// thật (5/phút) hoặc giới hạn của môi trường Development (1000/phút) đều không
/// thử được trong một test hợp lý.
/// </summary>
public class LoginRateLimitTests : IDisposable
{
    private const int GioiHan = 2;

    private readonly WebApplicationFactory<Program> _factory;

    public LoginRateLimitTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Ghi đè bằng in-memory config, KHÔNG sửa appsettings: sửa file
                // thì test này đổi hành vi của cả ứng dụng khi chạy tay.
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["RateLimiting:LoginPermitLimit"] = GioiHan.ToString()
                    }));
            });
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Vuot_gioi_han_thi_bi_tra_429_chu_khong_phai_503()
    {
        using var client = CreateClient();

        var maTrangThai = new List<HttpStatusCode>();

        for (var i = 0; i < GioiHan + 1; i++)
        {
            var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("Username", "ai-do"),
                new KeyValuePair<string, string>("Password", "mat-khau-sai")
            ]));

            maTrangThai.Add(response.StatusCode);
        }

        // GioiHan request đầu KHÔNG bị chặn bởi rate limiter. Chúng vẫn có thể
        // trả 400 vì thiếu antiforgery token - điều đó không sao: rate limiter là
        // MIDDLEWARE, chạy trước filter, nên permit vẫn bị tiêu đúng như thật.
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, maTrangThai[..GioiHan]);

        // Mặc định của framework là 503 Service Unavailable - sai nghĩa vì server
        // vẫn khoẻ. Phải cấu hình RejectionStatusCode thành 429.
        Assert.Equal(HttpStatusCode.TooManyRequests, maTrangThai[GioiHan]);
    }

    [Fact]
    public async Task GET_trang_dang_nhap_KHONG_bi_gioi_han()
    {
        using var client = CreateClient();

        // Xem trang đăng nhập là hành vi bình thường; chỉ việc THỬ mật khẩu mới
        // cần giới hạn. Đặt policy ở cấp class thì GET cũng bị chặn oan.
        for (var i = 0; i < GioiHan + 3; i++)
        {
            var response = await client.GetAsync("/Account/Login");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Cac_endpoint_khac_KHONG_bi_gioi_han_boi_policy_login()
    {
        using var client = CreateClient();

        for (var i = 0; i < GioiHan + 3; i++)
        {
            var response = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    public void Dispose() => _factory.Dispose();
}
