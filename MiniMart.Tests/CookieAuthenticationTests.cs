using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MiniMart.Tests;

/// <summary>
/// Integration test: boot chính Program.cs thật rồi đọc cấu hình đã được DI
/// phân giải. Cách này kiểm tra đúng thứ đang chạy, thay vì chép lại cấu hình
/// sang test rồi tự kiểm tra bản chép.
/// </summary>
public class CookieAuthenticationTests
{
    private static WebApplicationFactory<Program> CreateFactory(string environment) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);

                // ★ Bắt buộc từ khi VnPayOptions có ValidateOnStart.
                //
                // User Secrets CHỈ được nạp ở environment Development. Test này cố ý
                // boot ở "Production" nên khoá VNPay biến mất và ứng dụng từ chối khởi
                // động - đúng như thiết kế, và cũng đúng như chuyện sẽ xảy ra trên máy
                // chủ thật nếu quên đặt biến môi trường.
                //
                // Cấp cấu hình ngay tại đây thay vì sửa file, cùng cách LoginRateLimitTests
                // tự hạ hạn mức: test tự lo môi trường của mình, không ai phải nhớ giữ
                // một giá trị trong appsettings.json chỉ để test không đỏ.
                builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["VnPay:TmnCode"] = "TEST_TMNCODE",
                        ["VnPay:HashSecret"] = "TEST_HASHSECRET",
                        ["VnPay:BaseUrl"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                        ["VnPay:ReturnUrl"] = "http://localhost/Payment/Return"
                    }));
            });

    private static CookieAuthenticationOptions GetCookieOptions(string environment)
    {
        using var factory = CreateFactory(environment);

        return factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    [Fact]
    public async Task Ung_dung_phai_phuc_vu_duoc_request_sau_khi_them_auth_middleware()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        // Smoke test: pipeline có UseAuthentication vẫn xử lý request bình thường.
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Scheme_mac_dinh_phai_la_Cookies()
    {
        using var factory = CreateFactory("Development");

        // Hỏi đúng thứ framework dùng lúc chạy: AddAuthentication("Cookies") chỉ
        // set DefaultScheme, còn DefaultAuthenticateScheme để null rồi fallback
        // về nó - nên phải kiểm tra scheme đã phân giải, không phải trường thô.
        var scheme = await factory.Services
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetDefaultAuthenticateSchemeAsync();

        // Sai chỗ này thì [Authorize] trần sẽ không biết dùng scheme nào.
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, scheme?.Name);
    }

    [Fact]
    public void Duong_dan_redirect_phai_phan_biet_chua_dang_nhap_va_sai_quyen()
    {
        var options = GetCookieOptions("Development");

        // 401 -> LoginPath, 403 -> AccessDeniedPath. Trỏ chung một chỗ sẽ khiến
        // user đã đăng nhập bị đá về trang login khi thiếu quyền.
        Assert.Equal("/Account/Login", options.LoginPath);
        Assert.Equal("/Account/AccessDenied", options.AccessDeniedPath);
        Assert.NotEqual(options.LoginPath, options.AccessDeniedPath);
    }

    [Fact]
    public void Cookie_phai_bat_HttpOnly_va_SameSite_Lax()
    {
        var options = GetCookieOptions("Development");

        Assert.True(options.Cookie.HttpOnly);              // chống XSS đọc cookie
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite); // chống CSRF
        Assert.Equal("MiniMart.Auth", options.Cookie.Name);
    }

    [Fact]
    public void Phien_dang_nhap_phai_song_8_tieng_va_tu_gia_han()
    {
        var options = GetCookieOptions("Development");

        Assert.Equal(TimeSpan.FromHours(8), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
    }

    [Fact]
    public void SecurePolicy_trong_Development_phai_la_SameAsRequest()
    {
        var options = GetCookieOptions("Development");

        // Ép Always khi dev chạy profile http sẽ khiến trình duyệt không gửi
        // cookie -> đăng nhập thất bại im lặng, không có lỗi nào hiện ra.
        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void SecurePolicy_ngoai_Development_phai_la_Always()
    {
        var options = GetCookieOptions("Production");

        // Production bắt buộc cookie chỉ đi qua HTTPS.
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }
}
