using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Application.Interfaces;
using MiniMart.Application.Models;
using MiniMart.Common;
using MiniMart.Domain.Entities;
using MiniMart.Web.Controllers;
using MiniMart.Web.Middleware;

namespace MiniMart.Tests;

/// <summary>
/// Lưới cuối cùng cho exception chưa được xử lý.
///
/// <para>
/// Cách dựng lỗi: thay một service thật bằng một stub luôn ném. Như vậy exception đi ra
/// từ đúng chỗ nó sẽ đi ra trong thực tế - bên trong action của Controller, sau khi
/// routing, authentication và mọi middleware khác đã chạy. Map một endpoint giả bằng
/// <c>builder.Configure(...)</c> sẽ thay luôn cả pipeline, tức đo một pipeline không ai dùng.
/// </para>
/// </summary>
public class GlobalExceptionMiddlewareTests
{
    private const string LoiCoY = "LOI_CO_Y_DE_TEST_MIDDLEWARE";

    // ───────────── Đường HTML (trình duyệt) ─────────────

    [Fact]
    public async Task Loi_chua_xu_ly_tra_500_kem_trang_loi_than_thien()
    {
        using var factory = TaoFactoryHong();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString(),
            StringComparison.Ordinal);

        // Câu chữ dành cho NGƯỜI, không phải cho lập trình viên.
        Assert.Contains("Đã xảy ra lỗi", html, StringComparison.Ordinal);
        Assert.Contains("Quay về trang chủ", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Trang_loi_luon_co_MA_TRUY_VET()
    {
        using var factory = TaoFactoryHong();
        using var client = factory.CreateClient();

        var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // Không có mã truy vết thì người dùng chỉ báo được "trang bị lỗi", và không ai
        // tìm được dòng log tương ứng giữa hàng nghìn dòng. Đây là thứ DUY NHẤT nối
        // được màn hình của họ với stack trace đã bị giấu đi.
        Assert.Contains("<code>", html, StringComparison.Ordinal);
        Assert.Matches("<code>[^<]+</code>", html);
    }

    // ───────────── Đường JSON ─────────────

    [Fact]
    public async Task Client_gui_Accept_json_thi_nhan_JSON_khong_phai_HTML()
    {
        using var factory = TaoFactoryHong();
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");

        // Đúng hình dạng trình duyệt gửi khi gọi fetch(): cả một danh sách kèm q-value.
        // So bằng Accept == "application/json" sẽ trượt ở đây - đó là lý do middleware
        // dùng Contains, cùng quy ước với các endpoint giỏ hàng.
        request.Headers.Accept.ParseAdd("application/json, text/plain, */*;q=0.8");

        var response = await client.SendAsync(request);
        var than = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("application/json", response.Content.Headers.ContentType!.ToString(),
            StringComparison.Ordinal);

        // Phải parse được - dán HTML vào chỗ client gọi response.json() là nó ném ở một
        // chỗ chẳng liên quan gì tới lỗi thật.
        var json = JsonSerializer.Deserialize<Dictionary<string, string>>(than)!;
        Assert.True(json.ContainsKey("error"));
        Assert.False(string.IsNullOrWhiteSpace(json["traceId"]));
    }

    [Fact]
    public async Task Endpoint_IPN_tra_JSON_du_KHONG_co_header_Accept()
    {
        using var factory = TaoFactoryHongIpn();
        using var client = factory.CreateClient();

        // Cố ý KHÔNG gửi Accept: máy chủ VNPay là một chương trình, không phải trình
        // duyệt, và nó không cam kết gửi header nào. Thương lượng theo Accept một mình
        // sẽ trả trang HTML tiếng Việt cho nó.
        var request = new HttpRequestMessage(HttpMethod.Get, "/Payment/IpnAction?vnp_TxnRef=1");
        request.Headers.Accept.Clear();

        var response = await client.SendAsync(request);

        Assert.Contains("application/json", response.Content.Headers.ContentType!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void IpnAction_phai_mang_attribute_JsonErrorResponse()
    {
        var action = typeof(PaymentController).GetMethod(nameof(PaymentController.IpnAction))!;

        // Test CẤU TRÚC. Test hành vi ở trên chứng minh hôm nay đúng; test này canh giữ
        // chính LÝ DO nó đúng - gỡ attribute đi thì endpoint lặng lẽ quay về trả HTML.
        Assert.NotEmpty(action.GetCustomAttributes(typeof(JsonErrorResponseAttribute), inherit: true));
    }

    // ───────────── Không lộ thông tin ở Production ─────────────

    [Fact]
    public async Task Production_KHONG_lo_stack_trace_ra_ngoai()
    {
        using var factory = TaoFactoryHong("Production");
        using var client = factory.CreateClient();

        var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // ★ Ràng buộc quan trọng nhất của cả file. Stack trace nêu tên class, đường dẫn
        // file trên máy chủ và cấu trúc thư mục - tấm bản đồ cho người muốn tấn công.
        Assert.DoesNotContain(LoiCoY, html, StringComparison.Ordinal);
        Assert.DoesNotContain("MiniMart.Application", html, StringComparison.Ordinal);
        Assert.DoesNotContain("at ", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", html, StringComparison.Ordinal);

        // Nhưng mã truy vết thì VẪN phải có: giấu hết mọi thứ là người dùng gọi lên hỗ
        // trợ mà không nói được gì, còn ta không tra được log của đúng request đó.
        Assert.Matches("<code>[^<]+</code>", html);
    }

    [Fact]
    public async Task Production_JSON_cung_KHONG_lo_chi_tiet()
    {
        using var factory = TaoFactoryHong("Production");
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var than = await (await client.SendAsync(request)).Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<Dictionary<string, string>>(than)!;

        // Rò rỉ qua JSON dễ bị bỏ sót hơn qua HTML vì không ai nhìn nó bằng mắt.
        Assert.False(json.ContainsKey("detail"));
        Assert.DoesNotContain(LoiCoY, than, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_thi_CO_chi_tiet_de_con_debug_duoc()
    {
        using var factory = TaoFactoryHong();
        using var client = factory.CreateClient();

        var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // Đánh đổi có chủ ý: middleware này chạy ở CẢ hai môi trường, nên nó thay luôn
        // trang lỗi chi tiết mặc định của ASP.NET Core. Bù lại bằng việc tự in chi tiết
        // ở Development. Cách kia - chỉ đăng ký ở Production - nghe an toàn hơn nhưng
        // nghĩa là không test nào chạm tới middleware ở môi trường nó thật sự chạy.
        Assert.Contains(LoiCoY, html, StringComparison.Ordinal);
    }

    // ───────────── Dựng môi trường ─────────────

    private static WebApplicationFactory<Program> TaoFactoryHong(string moiTruong = "Development") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(moiTruong);
            CapCauHinhVnPay(builder, moiTruong);

            // HomeController.Index gọi ICategoryService -> stub ném -> exception đi ra
            // đúng như một bug thật.
            builder.ConfigureServices(services =>
                services.AddScoped<ICategoryService, CategoryServiceLuonNem>());
        });

    private static WebApplicationFactory<Program> TaoFactoryHongIpn() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
                services.AddScoped<IPaymentService, PaymentServiceLuonNem>());
        });

    /// <summary>
    /// Test boot ở Production phải TỰ cấp cấu hình VNPay: User Secrets chỉ được nạp ở
    /// Development, nên nếu không có thì <c>ValidateOnStart</c> làm ứng dụng từ chối
    /// khởi động - đúng như thiết kế, và test sẽ đỏ vì một lý do chẳng liên quan.
    /// </summary>
    private static void CapCauHinhVnPay(IWebHostBuilder builder, string moiTruong)
    {
        if (moiTruong == "Development")
        {
            return;
        }

        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["VnPay:TmnCode"] = "TEST_TMNCODE",
                ["VnPay:HashSecret"] = "TEST_HASHSECRET",
                ["VnPay:BaseUrl"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                ["VnPay:ReturnUrl"] = "http://localhost/Payment/Return"
            }));
    }

    private sealed class CategoryServiceLuonNem : ICategoryService
    {
        public Task<List<Category>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(LoiCoY);

        public Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(LoiCoY);

        public Task<Category> CreateAsync(string name, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(LoiCoY);

        public Task UpdateAsync(int id, string name, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(LoiCoY);

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(LoiCoY);
    }

    private sealed class PaymentServiceLuonNem : IPaymentService
    {
        public Task<IpnResult> XuLyIpnAsync(
            IReadOnlyDictionary<string, string?> thamSo,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(LoiCoY);

        public Task<string> TaoUrlThanhToanAsync(
            int orderId, int userId, string diaChiIp, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(LoiCoY);
    }
}
