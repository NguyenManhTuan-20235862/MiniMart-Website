using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Payments;

namespace MiniMart.Tests;

/// <summary>
/// Dựng URL thanh toán VNPay - unit test thuần, không DB, không mạng.
///
/// <para>
/// Toàn bộ class đang test là "ghép chuỗi rồi băm", nên nó test được TRỌN VẸN mà
/// không cần hạ tầng gì. Đó cũng là lý do tách nó khỏi Controller: nhét phép ký vào
/// Controller là biến một thứ kiểm được bằng phép tính thành thứ chỉ kiểm được bằng
/// cách bắn HTTP.
/// </para>
/// <para>
/// ⚠ Giới hạn thật, phải nói rõ: bộ test này chứng minh chữ ký ĐÚNG THEO ĐẶC TẢ mà
/// code đang hiểu. Nó KHÔNG chứng minh VNPay chấp nhận - điều đó chỉ có một giao dịch
/// sandbox thật với khoá thật mới trả lời được.
/// </para>
/// </summary>
public class VnPayServiceTests
{
    private const string TmnCode = "TMNTEST01";
    private const string HashSecret = "KHOA_BI_MAT_DE_TEST";
    private const string BaseUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    private const string ReturnUrl = "http://localhost:5231/Payment/Return";
    private const string ClientIp = "203.0.113.9";

    /// <summary>02:30 UTC = 09:30 giờ Việt Nam. Chọn giờ khiến hai múi ra ngày giống nhau
    /// nhưng GIỜ khác nhau, để lỗi "quên +7" lộ ra mà không bị che bởi việc đổi ngày.</summary>
    private static readonly DateTimeOffset LucChay = new(2026, 7, 27, 2, 30, 0, TimeSpan.Zero);

    private static readonly Order DonHang = new() { Id = 12345, TotalAmount = 1_250_000m };

    // ───────────── Thứ tự tham số ─────────────

    [Fact]
    public void Tham_so_duoc_sap_xep_dung_thu_tu_alphabet()
    {
        var query = LayPhanDuocKy(TaoUrl());

        var khoa = query.Split('&').Select(c => c.Split('=')[0]).ToArray();

        // Thứ tự này KHÔNG phải thứ tự tôi viết trong code - nó là thứ tự ordinal của
        // chính các tên khoá. Liệt kê tường minh ở đây để nếu ai đó thêm một tham số
        // mới vào giữa cho "dễ đọc", test đỏ ngay.
        Assert.Equal(
            new[]
            {
                "vnp_Amount", "vnp_Command", "vnp_CreateDate", "vnp_CurrCode",
                "vnp_ExpireDate", "vnp_IpAddr", "vnp_Locale", "vnp_OrderInfo",
                "vnp_OrderType", "vnp_ReturnUrl", "vnp_TmnCode", "vnp_TxnRef",
                "vnp_Version"
            },
            khoa);
    }

    [Fact]
    public void Thu_tu_la_ket_qua_SAP_XEP_chu_khong_phai_thu_tu_khai_bao()
    {
        var khoa = LayPhanDuocKy(TaoUrl())
            .Split('&')
            .Select(c => c.Split('=')[0])
            .ToArray();

        // Khẳng định TÍNH CHẤT thay vì một danh sách cụ thể: dãy phải tăng dần theo
        // so sánh Ordinal. Test trên khoá một danh sách cứng; test này khoá LÝ DO
        // danh sách đó đúng, nên nó vẫn có nghĩa khi tập tham số thay đổi.
        Assert.Equal(khoa.OrderBy(k => k, StringComparer.Ordinal).ToArray(), khoa);
    }

    [Fact]
    public void vnp_SecureHash_KHONG_nam_trong_phan_duoc_ky()
    {
        var url = TaoUrl();

        // Chữ ký là KẾT QUẢ của phép băm nên không thể là đầu vào của chính nó. Lỡ
        // đưa vào thì VNPay băm một tập tham số khác ta -> luôn lệch chữ ký.
        Assert.DoesNotContain("vnp_SecureHash", LayPhanDuocKy(url), StringComparison.Ordinal);

        // Và nó phải là tham số CUỐI CÙNG của URL.
        Assert.Matches(@"&vnp_SecureHash=[0-9a-f]{128}$", url);
    }

    // ───────────── Chữ ký ─────────────

    [Fact]
    public void Chu_ky_khop_voi_HMAC_SHA512_tinh_DOC_LAP()
    {
        var url = TaoUrl();

        // Dựng lại chuỗi cần ký bằng ĐƯỜNG KHÁC: viết tay đúng thứ tự, không dùng
        // SortedDictionary. Nếu code sắp xếp sai hoặc mã hoá khác đi, hai chuỗi lệch
        // nhau và hai chữ ký lệch nhau.
        var mongDoi = string.Join("&", new[]
        {
            $"vnp_Amount={Ma("125000000")}",
            $"vnp_Command={Ma("pay")}",
            $"vnp_CreateDate={Ma("20260727093000")}",
            $"vnp_CurrCode={Ma("VND")}",
            $"vnp_ExpireDate={Ma("20260727094500")}",
            $"vnp_IpAddr={Ma(ClientIp)}",
            $"vnp_Locale={Ma("vn")}",
            $"vnp_OrderInfo={Ma("Thanh toan don hang 12345")}",
            $"vnp_OrderType={Ma("other")}",
            $"vnp_ReturnUrl={Ma(ReturnUrl)}",
            $"vnp_TmnCode={Ma(TmnCode)}",
            $"vnp_TxnRef={Ma("12345")}",
            $"vnp_Version={Ma("2.1.0")}"
        });

        Assert.Equal(mongDoi, LayPhanDuocKy(url));
        Assert.Equal(HmacSha512(mongDoi, HashSecret), LayChuKy(url));
    }

    [Fact]
    public void Doi_khoa_bi_mat_thi_chu_ky_doi()
    {
        var chuKyA = LayChuKy(TaoUrl());
        var chuKyB = LayChuKy(TaoUrl(hashSecret: "MOT_KHOA_KHAC"));

        // Đây là toàn bộ lý do HMAC tồn tại thay vì SHA512 trần: không có khoá thì
        // không tạo được chữ ký hợp lệ. Nếu hai chữ ký này bằng nhau nghĩa là khoá
        // không hề tham gia phép băm.
        Assert.NotEqual(chuKyA, chuKyB);
    }

    [Theory]
    [InlineData(999)]        // đổi OrderId -> đổi vnp_TxnRef và vnp_OrderInfo
    [InlineData(12346)]
    public void Doi_bat_ky_tham_so_nao_cung_lam_doi_chu_ky(int orderId)
    {
        var goc = LayChuKy(TaoUrl());
        var khac = LayChuKy(TaoUrl(order: new Order { Id = orderId, TotalAmount = 1_250_000m }));

        // Chứng minh tham số THẬT SỰ nằm trong chuỗi được ký. Không có test này thì
        // một lỗi kiểu "quên thêm vnp_TxnRef vào dictionary" vẫn cho ra URL trông
        // hợp lệ và một chữ ký hợp lệ - của một tập tham số thiếu.
        Assert.NotEqual(goc, khac);
    }

    [Fact]
    public void Chu_ky_la_128_ky_tu_hex_thuong()
    {
        // SHA512 cho 64 byte = 128 ký tự hex. Sai độ dài nghĩa là dùng nhầm thuật
        // toán (SHA256 cho 64 ký tự) - VNPay từ chối và thông báo rất mơ hồ.
        Assert.Matches("^[0-9a-f]{128}$", LayChuKy(TaoUrl()));
    }

    // ───────────── Định dạng từng tham số ─────────────

    [Fact]
    public void So_tien_duoc_nhan_100_va_la_so_nguyen()
    {
        Assert.Equal("125000000", LayThamSo(TaoUrl(), "vnp_Amount"));
    }

    [Fact]
    public void So_tien_khong_dinh_dau_phan_cach_du_may_dang_dung_locale_vi_VN()
    {
        var goc = CultureInfo.CurrentCulture;

        try
        {
            // Máy dev en-US không tái hiện được lỗi này, nên phải ép culture trong test.
            CultureInfo.CurrentCulture = new CultureInfo("vi-VN");

            var soTien = LayThamSo(TaoUrl(), "vnp_Amount");

            // decimal.ToString() theo vi-VN sẽ ra "125000000,00" - dấu phẩy đi thẳng
            // vào chữ ký. Cùng họ với bẫy MoneyFormat/ToString("N0") đã gặp ở tầng Web.
            Assert.Equal("125000000", soTien);
            Assert.DoesNotContain(",", soTien, StringComparison.Ordinal);
            Assert.DoesNotContain(".", soTien, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = goc;
        }
    }

    [Fact]
    public void Thoi_gian_tao_theo_gio_Viet_Nam_khong_phai_UTC()
    {
        var url = TaoUrl();

        // 02:30 UTC + 7 = 09:30. Gửi UTC là VNPay thấy lệnh đến từ 7 tiếng trước và
        // coi như đã hết hạn.
        Assert.Equal("20260727093000", LayThamSo(url, "vnp_CreateDate"));
        Assert.NotEqual("20260727023000", LayThamSo(url, "vnp_CreateDate"));
    }

    [Fact]
    public void Han_thanh_toan_la_15_phut_sau_luc_tao()
    {
        Assert.Equal("20260727094500", LayThamSo(TaoUrl(), "vnp_ExpireDate"));
    }

    [Fact]
    public void TxnRef_la_OrderId_de_tra_nguoc_khi_VNPay_goi_ve()
    {
        Assert.Equal("12345", LayThamSo(TaoUrl(), "vnp_TxnRef"));
    }

    [Fact]
    public void Cac_tham_so_co_dinh_dung_dac_ta_2_1_0()
    {
        var url = TaoUrl();

        Assert.Equal("2.1.0", LayThamSo(url, "vnp_Version"));
        Assert.Equal("pay", LayThamSo(url, "vnp_Command"));
        Assert.Equal("VND", LayThamSo(url, "vnp_CurrCode"));
        Assert.Equal(TmnCode, LayThamSo(url, "vnp_TmnCode"));
        Assert.Equal(ClientIp, LayThamSo(url, "vnp_IpAddr"));
    }

    // ───────────── Mã hoá URL ─────────────

    [Fact]
    public void Gia_tri_duoc_ma_hoa_URL_trong_phan_duoc_ky()
    {
        var query = LayPhanDuocKy(TaoUrl());

        // ReturnUrl chứa "://" và "/". Không mã hoá thì dấu "&" hoặc "=" trong một
        // giá trị sẽ bị VNPay hiểu là ranh giới tham số - tách sai và sai chữ ký.
        Assert.DoesNotContain("vnp_ReturnUrl=http://", query, StringComparison.Ordinal);
        Assert.Contains($"vnp_ReturnUrl={Ma(ReturnUrl)}", query, StringComparison.Ordinal);
    }

    [Fact]
    public void Chu_ky_kiem_lai_duoc_tu_CHINH_query_string_da_gui()
    {
        var url = TaoUrl();

        // ★ Ràng buộc quan trọng nhất về mã hoá, và đây là bản ĐÃ SỬA sau mutation test.
        //
        // Bản đầu tiên khẳng định "phần được ký là tiền tố của query string" - và nó
        // TAUTOLOGY: cả hai vế đều bóc ra từ cùng một URL, nên nó xanh kể cả khi code
        // ký một chuỗi rồi gửi đi chuỗi mã hoá kiểu khác (%20 thay vì +).
        //
        // Cách duy nhất kiểm được từ bên ngoài là làm ĐÚNG việc máy chủ VNPay làm:
        // lấy query string thật sự được gửi, tự băm lại, so với chữ ký đính kèm.
        Assert.Equal(HmacSha512(LayPhanDuocKy(url), HashSecret), LayChuKy(url));
    }

    [Fact]
    public void URL_bat_dau_bang_BaseUrl_tu_cau_hinh()
    {
        // Không hardcode endpoint: sandbox và production khác URL, đổi môi trường
        // không được là sửa code rồi build lại.
        Assert.StartsWith(BaseUrl + "?", TaoUrl(), StringComparison.Ordinal);
    }

    // ───────────── Helper ─────────────

    private static string TaoUrl(
        Order? order = null,
        string? hashSecret = null)
    {
        var options = Options.Create(new VnPayOptions
        {
            TmnCode = TmnCode,
            HashSecret = hashSecret ?? HashSecret,
            BaseUrl = BaseUrl,
            ReturnUrl = ReturnUrl
        });

        var service = new VnPayService(options, new DongHoDung(LucChay));

        return service.CreatePaymentUrl(order ?? DonHang, ClientIp);
    }

    /// <summary>Phần query ĐỨNG TRƯỚC vnp_SecureHash - đúng chuỗi đã được đem đi băm.</summary>
    private static string LayPhanDuocKy(string url)
    {
        var batDau = url.IndexOf('?', StringComparison.Ordinal) + 1;
        var truocChuKy = url.IndexOf("&vnp_SecureHash=", StringComparison.Ordinal);

        // Assert cho chính helper: helper đọc sai thì mọi test dùng nó đều sai theo
        // một cách rất khó nhìn ra. Cùng bài học với helper đọc HTML bằng regex.
        Assert.True(truocChuKy > batDau, $"URL không có vnp_SecureHash ở cuối: {url}");

        return url[batDau..truocChuKy];
    }

    private static string LayChuKy(string url) =>
        url[(url.IndexOf("&vnp_SecureHash=", StringComparison.Ordinal) + "&vnp_SecureHash=".Length)..];

    private static string LayThamSo(string url, string ten)
    {
        var cap = LayPhanDuocKy(url)
            .Split('&')
            .Single(c => c.StartsWith(ten + "=", StringComparison.Ordinal));

        return WebUtility.UrlDecode(cap[(ten.Length + 1)..]);
    }

    private static string Ma(string giaTri) => WebUtility.UrlEncode(giaTri);

    private static string HmacSha512(string duLieu, string khoa)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(khoa));

        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(duLieu)));
    }

    /// <summary>
    /// Đồng hồ đứng yên. <c>vnp_CreateDate</c> đi vào chữ ký nên dùng đồng hồ thật là
    /// mỗi lần chạy ra một chữ ký khác - không có giá trị mong đợi nào viết được.
    /// </summary>
    private sealed class DongHoDung(DateTimeOffset luc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => luc;
    }
}
