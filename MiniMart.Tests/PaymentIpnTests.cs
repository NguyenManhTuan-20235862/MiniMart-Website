using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Enums;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// <c>GET /Payment/IpnAction</c> - kênh máy chủ sang máy chủ, trên SQL Server thật.
///
/// <para>
/// Cần DB thật (không mock) vì hai thứ quan trọng nhất ở đây là hành vi của database
/// engine: <c>UNIQUE(Payments.OrderId)</c> và tính nguyên tử của INSERT Payment +
/// UPDATE Order trong một <c>SaveChanges</c>.
/// </para>
/// </summary>
public class PaymentIpnTests : IAsyncLifetime
{
    private const string HashSecret = "KHOA_TEST_IPN";
    private const string TmnCode = "TMN_TEST_IPN";
    private const decimal TongTien = 1_250_000m;

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["VnPay:TmnCode"] = TmnCode,
                    ["VnPay:HashSecret"] = HashSecret,
                    ["VnPay:BaseUrl"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                    ["VnPay:ReturnUrl"] = "http://localhost/Payment/Return"
                })));

    private int _categoryId;
    private int _productId;
    private int _userId;
    private int _orderId;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"IPN_{Guid.NewGuid():N}"[..14] };
        var product = new Product { Name = "HangIpn", Price = TongTien, Stock = 100, Category = category };
        var user = new User { Username = $"ipn_{Guid.NewGuid():N}"[..16], PasswordHash = "x", Role = UserRole.Customer };

        context.Products.Add(product);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var order = new Order
        {
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            TotalAmount = TongTien,
            RecipientName = "Nguoi Nhan",
            RecipientPhone = "0912345678",
            ShippingAddress = "1 Dia Chi, Ha Noi",
            Items =
            {
                new OrderDetail
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    UnitPrice = TongTien,
                    Quantity = 1
                }
            }
        };

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productId = product.Id;
        _userId = user.Id;
        _orderId = order.Id;
    }

    // ───────────── Đường thành công ─────────────

    [Fact]
    public async Task IPN_hop_le_thi_don_thanh_Paid_va_co_ban_ghi_Payment()
    {
        var json = await GoiAsync(TaoThamSo());

        Assert.Equal("00", json.GetProperty("rspCode").GetString());

        var (status, payment) = await DocDbAsync();

        Assert.Equal(OrderStatus.Paid, status);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Succeeded, payment!.Status);
        Assert.Equal(TongTien, payment.Amount);
    }

    [Fact]
    public async Task Response_dung_dinh_dang_VNPay_yeu_cau()
    {
        var json = await GoiAsync(TaoThamSo());

        // Hợp đồng do VNPay định nghĩa: JSON có đúng hai trường RspCode và Message.
        // System.Text.Json tự đổi sang camelCase ở phía ta.
        Assert.Equal("00", json.GetProperty("rspCode").GetString());
        Assert.Equal("Confirm Success", json.GetProperty("message").GetString());
        Assert.Equal(2, json.EnumerateObject().Count());
    }

    [Fact]
    public async Task Luon_tra_HTTP_200_ke_ca_khi_tu_choi()
    {
        var thamSo = TaoThamSo();
        thamSo["vnp_SecureHash"] = new string('a', 128);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(DuongDan(thamSo));

        // VNPay đọc mã trong THÂN response, không đọc mã HTTP. Trả 400 cho một chữ ký
        // sai sẽ khiến họ coi là lỗi tầng vận chuyển và gửi lại mãi một thông báo
        // không bao giờ hợp lệ.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("97", json.GetProperty("rspCode").GetString());
    }

    // ───────────── ★ Đối chiếu số tiền ─────────────

    [Fact]
    public async Task So_tien_khong_khop_thi_tra_04_va_don_VAN_Pending()
    {
        // Chữ ký được ký lại HỢP LỆ trên chính số tiền sai này - đây là điểm cốt lõi:
        // chữ ký hợp lệ KHÔNG có nghĩa số tiền đúng.
        var json = await GoiAsync(TaoThamSo(amount: "100"));

        Assert.Equal("04", json.GetProperty("rspCode").GetString());
        Assert.Equal("Invalid amount", json.GetProperty("message").GetString());

        var (status, payment) = await DocDbAsync();

        // Không có lệnh kiểm này thì đơn 1.250.000 vừa được đánh dấu đã thanh toán
        // bằng 1 đồng - không exception, không log lỗi, không gì cả.
        Assert.Equal(OrderStatus.Pending, status);
        Assert.Null(payment);
    }

    [Fact]
    public async Task Quen_chia_100_cung_bi_bat()
    {
        // 125000000 là số VNPay gửi (đã nhân 100). Nếu code quên chia lại thì nó sẽ so
        // 125.000.000 với 1.250.000 và... vẫn lệch, nên vẫn bị bắt. Test này khoá
        // chiều ngược lại: cổng báo đúng số đã nhân, ta chia đúng, hai bên khớp.
        var json = await GoiAsync(TaoThamSo(amount: "12500000000"));

        Assert.Equal("04", json.GetProperty("rspCode").GetString());
    }

    // ───────────── Đơn không tồn tại ─────────────

    [Fact]
    public async Task TxnRef_tro_toi_don_khong_ton_tai_thi_tra_01()
    {
        var json = await GoiAsync(TaoThamSo(txnRef: "999999999"));

        Assert.Equal("01", json.GetProperty("rspCode").GetString());
    }

    // ───────────── Idempotent ─────────────

    [Fact]
    public async Task Goi_IPN_hai_lan_thi_lan_hai_tra_02_va_KHONG_ghi_them()
    {
        var lanMot = await GoiAsync(TaoThamSo());
        var lanHai = await GoiAsync(TaoThamSo());

        Assert.Equal("00", lanMot.GetProperty("rspCode").GetString());
        Assert.Equal("02", lanHai.GetProperty("rspCode").GetString());

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Đúng MỘT bản ghi thanh toán. Hai bản là nhân đôi doanh thu trong mọi báo cáo.
        Assert.Equal(1, await context.Payments.CountAsync(p => p.OrderId == _orderId));
    }

    [Fact]
    public async Task UNIQUE_index_chan_ban_ghi_thanh_toan_thu_hai()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        context.Payments.Add(TaoPayment());
        await context.SaveChangesAsync();

        context.Payments.Add(TaoPayment());

        // Ràng buộc DB là bảo đảm CUỐI, độc lập với mọi lệnh kiểm ở Service. Đây là
        // thứ chịu được hai IPN chạy song song - lệnh kiểm ở Service thì không.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    // ───────────── Giao dịch thất bại ─────────────

    [Fact]
    public async Task Giao_dich_that_bai_van_tra_00_nhung_don_KHONG_thanh_Paid()
    {
        var json = await GoiAsync(TaoThamSo(responseCode: "51", transactionStatus: "02"));

        // 00 = "tôi đã nhận và xử lý xong thông báo", không phải "giao dịch thành công".
        Assert.Equal("00", json.GetProperty("rspCode").GetString());

        var (status, payment) = await DocDbAsync();

        Assert.Equal(OrderStatus.Pending, status);
        Assert.Equal(PaymentStatus.Failed, payment!.Status);
        Assert.Equal("51", payment.ResponseCode);
    }

    // ───────────── Return KHÔNG được ghi DB ─────────────

    [Fact]
    public async Task Return_voi_chu_ky_hop_le_bao_thanh_cong_VAN_KHONG_doi_gi_trong_DB()
    {
        var thamSo = TaoThamSo();
        thamSo["vnp_SecureHash"] = Ky(thamSo);

        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync(
            "/Payment/Return?" + string.Join("&", thamSo.Select(c =>
                $"{c.Key}={WebUtility.UrlEncode(c.Value)}")));

        // Trang vẫn báo thành công cho khách xem...
        Assert.Contains("Thanh toán thành công", html, StringComparison.Ordinal);

        var (status, payment) = await DocDbAsync();

        // ...nhưng KHÔNG ghi gì. Đây là test HÀNH VI cho quy tắc mà trước đây chỉ khoá
        // được bằng test cấu trúc - hồi đó Order chưa có cột Status nào để quan sát.
        //
        // Lý do quy tắc tồn tại: request này đi qua trình duyệt khách nên có thể không
        // bao giờ tới (đóng tab, mất mạng). Ghi nhận thanh toán ở đây là chấp nhận
        // việc mất hẳn những đơn mà khách đã trả tiền.
        Assert.Equal(OrderStatus.Pending, status);
        Assert.Null(payment);
    }

    // ───────────── Helper ─────────────

    private Dictionary<string, string> TaoThamSo(
        string? amount = null,
        string? txnRef = null,
        string responseCode = "00",
        string transactionStatus = "00") =>
        new(StringComparer.Ordinal)
        {
            // VNPay gửi số tiền ĐÃ nhân 100.
            ["vnp_Amount"] = amount ?? ((long)(TongTien * 100m)).ToString(),
            ["vnp_BankCode"] = "NCB",
            ["vnp_OrderInfo"] = $"Thanh toan don hang {_orderId}",
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_TmnCode"] = TmnCode,
            ["vnp_TransactionNo"] = "14200000",
            ["vnp_TransactionStatus"] = transactionStatus,
            ["vnp_TxnRef"] = txnRef ?? _orderId.ToString()
        };

    private Payment TaoPayment() => new()
    {
        OrderId = _orderId,
        Status = PaymentStatus.Succeeded,
        Amount = TongTien,
        TransactionNo = "1",
        BankCode = "NCB",
        ResponseCode = "00",
        CreatedAt = DateTime.UtcNow
    };

    private async Task<JsonElement> GoiAsync(Dictionary<string, string> thamSo)
    {
        using var client = _factory.CreateClient();

        var body = await client.GetStringAsync(DuongDan(thamSo));

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string DuongDan(Dictionary<string, string> thamSo)
    {
        if (!thamSo.ContainsKey("vnp_SecureHash"))
        {
            thamSo["vnp_SecureHash"] = Ky(thamSo);
        }

        return "/Payment/IpnAction?" + string.Join("&", thamSo.Select(c =>
            $"{c.Key}={WebUtility.UrlEncode(c.Value)}"));
    }

    /// <summary>Ký y như phía VNPay ký - viết lại, không gọi VnPayService.</summary>
    private static string Ky(Dictionary<string, string> thamSo)
    {
        var deKy = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (khoa, giaTri) in thamSo)
        {
            if (khoa is "vnp_SecureHash" or "vnp_SecureHashType")
            {
                continue;
            }

            deKy[khoa] = giaTri;
        }

        var duLieu = string.Join("&", deKy.Select(c => $"{c.Key}={WebUtility.UrlEncode(c.Value)}"));

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(HashSecret));

        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(duLieu)));
    }

    private async Task<(OrderStatus Status, Payment? Payment)> DocDbAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var status = await context.Orders
            .AsNoTracking()
            .Where(o => o.Id == _orderId)
            .Select(o => o.Status)
            .SingleAsync();

        var payment = await context.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == _orderId);

        return (status, payment);
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Con lên cha: Payment -> OrderDetail -> Order -> User, rồi Product -> Category.
        await context.Payments.Where(p => p.OrderId == _orderId).ExecuteDeleteAsync();
        await context.OrderDetails.Where(d => d.OrderId == _orderId).ExecuteDeleteAsync();
        await context.Orders.Where(o => o.Id == _orderId).ExecuteDeleteAsync();
        await context.Users.Where(u => u.Id == _userId).ExecuteDeleteAsync();
        await context.Products.Where(p => p.Id == _productId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
