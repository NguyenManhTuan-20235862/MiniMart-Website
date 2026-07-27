using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MiniMart.Infrastructure.Payments;

namespace MiniMart.Tests;

/// <summary>
/// Cấu hình VNPay: binding, validate lúc khởi động, và ràng buộc quan trọng nhất -
/// <b>khoá bí mật không được nằm trong repo</b>.
/// </summary>
public class VnPayOptionsTests
{
    private static readonly VnPayOptions HopLe = new()
    {
        TmnCode = "TMN123",
        HashSecret = "KHOA_BI_MAT",
        BaseUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
        ReturnUrl = "http://localhost:5231/Payment/Return"
    };

    // ───────────── Bí mật không được vào Git ─────────────

    [Fact]
    public void appsettings_json_TUYET_DOI_khong_chua_HashSecret()
    {
        var json = File.ReadAllText(DuongDan("appsettings.json"));

        using var tai_lieu = JsonDocument.Parse(json);

        var vnpay = tai_lieu.RootElement.GetProperty(VnPayOptions.SectionName);

        // ★ Test có giá trị NHẤT trong file này, và nó là test CẤU TRÚC.
        //
        // Nó không kiểm tra hành vi nào cả - nó canh giữ một quy ước mà bình thường
        // chẳng có gì bắt lỗi: thêm "HashSecret": "abc..." vào appsettings.json thì
        // ứng dụng chạy TỐT HƠN (hết lỗi cấu hình), mọi test khác xanh, và bí mật
        // vừa được commit vào lịch sử Git vĩnh viễn.
        //
        // Khoá bình luận "// HashSecret" thì được phép - nó là tài liệu, không phải giá trị.
        Assert.False(
            vnpay.TryGetProperty("HashSecret", out _),
            "appsettings.json nằm trong Git. Đặt HashSecret vào User Secrets (dev) "
            + "hoặc biến môi trường VnPay__HashSecret (triển khai/CI).");
    }

    [Fact]
    public void appsettings_Development_json_cung_khong_chua_HashSecret()
    {
        var json = File.ReadAllText(DuongDan("appsettings.Development.json"));

        // File Development cũng nằm trong Git. Đây là chỗ rất dễ sa ngã vì nó "chỉ
        // dành cho dev" - nhưng dev và Git là hai chuyện khác nhau.
        Assert.DoesNotContain("HashSecret", json, StringComparison.OrdinalIgnoreCase);
    }

    // ───────────── Binding ─────────────

    [Fact]
    public void Cau_hinh_duoc_bind_vao_VnPayOptions_khi_ung_dung_chay()
    {
        using var factory = new WebApplicationFactory<Program>();

        var options = factory.Services.GetRequiredService<IOptions<VnPayOptions>>().Value;

        // Boot Program.cs thật rồi đọc lại - kiểm đúng thứ đang chạy, không phải một
        // bản chép của cấu hình. Sai tên section thì mọi trường về rỗng.
        Assert.False(string.IsNullOrWhiteSpace(options.TmnCode));
        Assert.False(string.IsNullOrWhiteSpace(options.HashSecret));
        Assert.Equal("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html", options.BaseUrl);
        Assert.Contains("/Payment/Return", options.ReturnUrl);
    }

    [Fact]
    public void BaseUrl_va_ReturnUrl_lay_tu_appsettings_chu_khong_hardcode()
    {
        var json = File.ReadAllText(DuongDan("appsettings.json"));

        using var tai_lieu = JsonDocument.Parse(json);
        var vnpay = tai_lieu.RootElement.GetProperty(VnPayOptions.SectionName);

        // Hai giá trị này KHÔNG bí mật nhưng vẫn phải ở cấu hình: URL sandbox và URL
        // production khác nhau, và đổi môi trường không được là đổi code rồi build lại.
        Assert.True(vnpay.TryGetProperty("BaseUrl", out _));
        Assert.True(vnpay.TryGetProperty("ReturnUrl", out _));
    }

    // ───────────── Validate lúc khởi động ─────────────

    [Fact]
    public void Thieu_khoa_bi_mat_thi_ung_dung_TU_CHOI_KHOI_DONG()
    {
        // Environment "Production" -> User Secrets KHÔNG được nạp, và không cấp cấu
        // hình in-memory nào. Đây đúng là hình dạng một lần triển khai bị quên biến
        // môi trường.
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        // ★ Test này tồn tại vì một mutation đã LỌT LƯỚI: bỏ .ValidateOnStart() đi thì
        // toàn bộ 399 test khác vẫn xanh. Options được tạo LƯỜI nên không ai chạm tới
        // VnPayOptions trong test là không ai phát hiện cấu hình hỏng - y hệt việc
        // ứng dụng thật chạy bình thường cho tới request thanh toán đầu tiên.
        var loi = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("HashSecret", loi.Message);
    }

    [Fact]
    public void Cau_hinh_du_thi_hop_le()
    {
        var ketQua = new VnPayOptionsValidator().Validate(null, HopLe);

        Assert.True(ketQua.Succeeded);
    }

    [Theory]
    [InlineData(nameof(VnPayOptions.TmnCode))]
    [InlineData(nameof(VnPayOptions.HashSecret))]
    [InlineData(nameof(VnPayOptions.BaseUrl))]
    [InlineData(nameof(VnPayOptions.ReturnUrl))]
    public void Thieu_bat_ky_truong_nao_cung_khong_hop_le(string truongBoTrong)
    {
        var options = XoaTruong(truongBoTrong);

        var ketQua = new VnPayOptionsValidator().Validate(null, options);

        Assert.True(ketQua.Failed);
        Assert.Contains(truongBoTrong, ketQua.FailureMessage);
    }

    [Fact]
    public void Thong_bao_loi_HashSecret_phai_chi_ro_CACH_KHAC_PHUC()
    {
        var ketQua = new VnPayOptionsValidator().Validate(null, XoaTruong(nameof(VnPayOptions.HashSecret)));

        // Người đọc thông báo này là người đang KHÔNG biết phải làm gì. Nêu tên khoá
        // bị thiếu mà không nêu cách khai báo là bắt họ đi tra tài liệu.
        Assert.Contains("user-secrets", ketQua.FailureMessage);
        Assert.Contains("VnPay__HashSecret", ketQua.FailureMessage);
    }

    [Fact]
    public void URL_tuong_doi_bi_tu_choi()
    {
        var options = new VnPayOptions
        {
            TmnCode = HopLe.TmnCode,
            HashSecret = HopLe.HashSecret,
            BaseUrl = HopLe.BaseUrl,
            ReturnUrl = "/Payment/Return"   // thiếu scheme + host
        };

        var ketQua = new VnPayOptionsValidator().Validate(null, options);

        // Chỉ kiểm rỗng thì giá trị này lọt, rồi hỏng ở tận lúc ghép URL gửi sang VNPay -
        // xa chỗ gây lỗi và không còn manh mối nào chỉ về cấu hình.
        Assert.True(ketQua.Failed);
        Assert.Contains("tuyệt đối", ketQua.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bao_TAT_CA_loi_trong_MOT_lan_thay_vi_dung_o_loi_dau_tien()
    {
        var ketQua = new VnPayOptionsValidator().Validate(null, new VnPayOptions());

        // Dừng ở lỗi đầu tiên thì người cấu hình phải chạy lại bốn lần mới biết hết.
        Assert.True(ketQua.Failed);
        Assert.Equal(4, ketQua.Failures.Count());
    }

    // ───────────── Helper ─────────────

    private static VnPayOptions XoaTruong(string ten)
    {
        var options = new VnPayOptions
        {
            TmnCode = HopLe.TmnCode,
            HashSecret = HopLe.HashSecret,
            BaseUrl = HopLe.BaseUrl,
            ReturnUrl = HopLe.ReturnUrl
        };

        typeof(VnPayOptions).GetProperty(ten)!.SetValue(options, string.Empty);

        return options;
    }

    /// <summary>
    /// Đường dẫn tới file cấu hình của MiniMart.Web.
    ///
    /// <para>
    /// Đi lên từ thư mục chạy test (<c>bin/Debug/net10.0</c>) chứ không dùng đường dẫn
    /// tuyệt đối: đường dẫn tuyệt đối là thứ chạy trên máy tôi và đỏ trên CI.
    /// </para>
    /// </summary>
    private static string DuongDan(string tenFile) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MiniMart.Web", tenFile);
}
