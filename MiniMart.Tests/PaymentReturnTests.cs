using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiniMart.Application.Interfaces;
using MiniMart.Domain.Interfaces;
using MiniMart.Domain.ValueObjects;
using MiniMart.Infrastructure.Payments;
using MiniMart.Web.Controllers;

namespace MiniMart.Tests;

/// <summary>
/// <c>GET /Payment/Return</c> - trang VNPay đưa khách quay về.
///
/// <para>
/// Hai nhóm ràng buộc được khoá ở đây, và nhóm thứ hai quan trọng hơn:
/// (1) chữ ký sai thì không được tin gì cả, và
/// (2) action này <b>không có đường nào để ghi DB</b> - kiểm bằng test CẤU TRÚC, vì
/// hôm nay chưa có cột nào để ghi nên test hành vi không nói lên điều gì.
/// </para>
/// </summary>
public class PaymentReturnTests
{
    private const string HashSecret = "KHOA_TEST_CUA_RIENG_BO_TEST_NAY";
    private const string TmnCode = "TMN_TEST";

    /// <summary>
    /// Factory tự cấp cấu hình VNPay, KHÔNG dựa vào User Secrets của máy đang chạy.
    ///
    /// <para>
    /// Bản đầu của bộ test này lấy thẳng giá trị trong User Secrets trên máy tôi. Nó
    /// xanh ở đây và sẽ đỏ ở mọi máy khác - CI dùng biến môi trường với giá trị khác
    /// hẳn. Test phụ thuộc trạng thái ngoài repo là test chỉ đúng ở đúng một chỗ.
    /// </para>
    /// </summary>
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

    // ───────────── Chữ ký phải được kiểm TRƯỚC ─────────────

    [Fact]
    public async Task Chu_ky_hop_le_va_ma_00_thi_bao_thanh_cong()
    {
        var html = await GoiAsync(TaoThamSo());

        Assert.Contains("Thanh toán thành công", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Khong_co_chu_ky_thi_KHONG_tin_gi_ca()
    {
        var thamSo = TaoThamSo();
        thamSo.Remove("vnp_SecureHash");

        var html = await GoiAsync(thamSo, kyLai: false);

        // Đây là request của một con bot quét URL, hoặc của người đang thử tay.
        Assert.Contains("Không xác nhận được kết quả", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Thanh toán thành công", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chu_ky_sai_thi_KHONG_tin_gi_ca()
    {
        var thamSo = TaoThamSo();
        thamSo["vnp_SecureHash"] = new string('a', 128);

        var html = await GoiAsync(thamSo, kyLai: false);

        Assert.Contains("Không xác nhận được kết quả", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("vnp_Amount", "1")]              // sửa số tiền
    [InlineData("vnp_TxnRef", "999999")]         // đổi sang đơn của người khác
    [InlineData("vnp_ResponseCode", "00")]       // ép "thành công"
    [InlineData("vnp_TransactionStatus", "00")]
    public async Task Sua_bat_ky_tham_so_nao_sau_khi_ky_deu_bi_phat_hien(string khoa, string giaTriMoi)
    {
        // Ký một tập tham số THẤT BẠI trước...
        var thamSo = TaoThamSo(responseCode: "51", transactionStatus: "02");
        thamSo["vnp_SecureHash"] = Ky(thamSo);

        // ...rồi sửa nội dung, giữ nguyên chữ ký cũ. Đây đúng là việc kẻ tấn công làm:
        // họ nhìn thấy toàn bộ URL trên thanh địa chỉ trình duyệt của chính mình.
        thamSo[khoa] = giaTriMoi;

        var html = await GoiAsync(thamSo, kyLai: false);

        // Không có khoá bí mật thì không ký lại được -> mọi chỉnh sửa đều lộ.
        Assert.Contains("Không xác nhận được kết quả", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Thanh toán thành công", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Khach_tu_huy_KHONG_duoc_hien_nhu_loi()
    {
        var html = await GoiAsync(TaoThamSo(responseCode: "24", transactionStatus: "02"));

        Assert.Contains("Đã huỷ giao dịch", html, StringComparison.Ordinal);

        // Huỷ là hành động chủ động của khách. Hiện "thất bại" cho nó là khiến họ
        // tưởng hệ thống có sự cố rồi gọi tổng đài.
        Assert.DoesNotContain("Thanh toán không thành công", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Giao_dich_loi_thi_bao_that_bai()
    {
        var html = await GoiAsync(TaoThamSo(responseCode: "51", transactionStatus: "02"));

        Assert.Contains("Thanh toán không thành công", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ma_ResponseCode_00_nhung_TransactionStatus_that_bai_thi_KHONG_thanh_cong()
    {
        var html = await GoiAsync(TaoThamSo(responseCode: "00", transactionStatus: "02"));

        // Phải kiểm CẢ HAI mã: ResponseCode là kết quả của lệnh gửi tới cổng, còn
        // TransactionStatus là kết quả của chính giao dịch.
        Assert.DoesNotContain("Thanh toán thành công", html, StringComparison.Ordinal);
    }

    // ───────────── Không được ghi DB ─────────────

    [Fact]
    public void PaymentController_KHONG_duoc_phu_thuoc_bat_ky_thu_gi_ghi_duoc_DB()
    {
        var thamSo = typeof(PaymentController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToArray();

        // ⚠ ĐÃ YẾU ĐI so với bản đầu, và cố ý ghi lại lý do.
        //
        // Bản đầu khẳng định constructor CHỈ có IVnPayService, tức không tồn tại đường
        // nào để ghi DB. Từ khi có IpnAction, controller buộc phải giữ thêm
        // IPaymentService - bảo đảm cấu trúc đó không còn.
        //
        // Bù lại bằng một bảo đảm MẠNH HƠN, viết được nhờ Order.Status đã tồn tại:
        // PaymentIpnTests.Return_voi_chu_ky_hop_le_bao_thanh_cong_VAN_KHONG_doi_gi_trong_DB
        // là test HÀNH VI thật. Test này giờ chỉ còn canh việc không ai tiêm thêm
        // IOrderService/IUnitOfWork/DbContext vào đây.
        //
        // ILogger được thêm vào danh sách MỘT CÁCH CÓ CHỦ Ý, không phải để test hết đỏ:
        // nó không có đường nào chạm database, nên nó không làm yếu điều đang được canh.
        //
        // Vẫn giữ dạng DANH SÁCH ĐẦY ĐỦ (allowlist) chứ không đổi sang "không được chứa
        // IUnitOfWork" (denylist): allowlist bắt cả những kiểu chưa ai nghĩ tới, còn
        // denylist chỉ bắt đúng những kiểu người viết nhớ liệt kê. Cái giá phải trả là
        // đúng cái vừa xảy ra - thêm một dependency vô hại cũng phải sửa test, và đó là
        // một lần dừng lại để cân nhắc, không phải phiền toái.
        Assert.Equal(
            new[] { typeof(IVnPayService), typeof(IPaymentService), typeof(ILogger<PaymentController>) },
            thamSo);
    }

    [Fact]
    public void Return_chi_nhan_GET()
    {
        var action = typeof(PaymentController).GetMethod(nameof(PaymentController.Return))!;

        // GET là ĐÚNG ở đây, ngược hẳn với mọi endpoint khác của dự án - vì action này
        // không ghi gì. Ngày nào nó bắt đầu ghi DB thì GET trở thành lỗ hổng: chỉ cần
        // nhúng <img src="..."> là kích hoạt được. Ràng buộc "chỉ đọc" và "là GET" đi
        // liền nhau, phá một cái là phải xét lại cái kia.
        Assert.NotNull(action.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpGetAttribute>());
    }

    // ───────────── Truy cập ─────────────

    [Fact]
    public async Task Khach_chua_dang_nhap_van_xem_duoc_ket_qua()
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(DuongDan(TaoThamSo()));

        // Phiên có thể đã hết hạn trong lúc khách thao tác ở ngân hàng. Đẩy sang trang
        // đăng nhập ngay sau khi vừa trả tiền là cách chắc chắn nhất khiến họ tưởng
        // giao dịch hỏng.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Trang_ket_qua_KHONG_lo_chi_tiet_don_hang()
    {
        var html = await GoiAsync(TaoThamSo());

        // Trang [AllowAnonymous] nên ai cầm URL cũng mở được - URL đó nằm trong lịch
        // sử trình duyệt. Số tiền và mã giao dịch ngân hàng không được hiện ở đây;
        // chi tiết đơn nằm sau /Checkout/Success/{id}, nơi vẫn lọc theo chủ sở hữu.
        Assert.DoesNotContain("12,500,000", html, StringComparison.Ordinal);
        Assert.DoesNotContain("14200000", html, StringComparison.Ordinal);   // vnp_TransactionNo
        Assert.Contains("/Checkout/Success/", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chu_ky_sai_thi_KHONG_link_sang_don_hang()
    {
        var thamSo = TaoThamSo();
        thamSo["vnp_SecureHash"] = new string('b', 128);

        var html = await GoiAsync(thamSo, kyLai: false);

        // Chữ ký sai nghĩa là KHÔNG có gì trong query đáng tin, kể cả vnp_TxnRef.
        // Dựng link từ nó là để người lạ tự chọn số đơn muốn thử.
        Assert.DoesNotContain("/Checkout/Success/", html, StringComparison.Ordinal);
    }

    // ───────────── Helper ─────────────

    private static Dictionary<string, string> TaoThamSo(
        string responseCode = "00",
        string transactionStatus = "00") =>
        new(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = "1250000000",
            ["vnp_BankCode"] = "NCB",
            ["vnp_OrderInfo"] = "Thanh toan don hang 12345",
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_TmnCode"] = TmnCode,
            ["vnp_TransactionNo"] = "14200000",
            ["vnp_TransactionStatus"] = transactionStatus,
            ["vnp_TxnRef"] = "12345"
        };

    private async Task<string> GoiAsync(Dictionary<string, string> thamSo, bool kyLai = true)
    {
        if (kyLai)
        {
            thamSo["vnp_SecureHash"] = Ky(thamSo);
        }

        using var client = _factory.CreateClient();

        return await client.GetStringAsync(DuongDan(thamSo, daKy: true));
    }

    private static string DuongDan(Dictionary<string, string> thamSo, bool daKy = false)
    {
        if (!daKy && !thamSo.ContainsKey("vnp_SecureHash"))
        {
            thamSo["vnp_SecureHash"] = Ky(thamSo);
        }

        var query = string.Join("&", thamSo.Select(c =>
            $"{c.Key}={WebUtility.UrlEncode(c.Value)}"));

        return "/Payment/Return?" + query;
    }

    /// <summary>
    /// Ký y như VNPay ký: bỏ vnp_SecureHash, sắp xếp ordinal, mã hoá URL, HMAC-SHA512.
    ///
    /// <para>
    /// Viết lại phép ký ở đây thay vì gọi <c>VnPayService.CreatePaymentUrl</c>: đây là
    /// mô phỏng phía ĐỐI TÁC. Dùng chính code đang test để tạo dữ liệu đầu vào thì test
    /// chỉ chứng minh code nhất quán với chính nó, kể cả khi cả hai chiều cùng sai.
    /// </para>
    /// </summary>
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

        var duLieu = string.Join("&", deKy.Select(c =>
            $"{c.Key}={WebUtility.UrlEncode(c.Value)}"));

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(HashSecret));

        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(duLieu)));
    }

    // ───────────── Verify ở tầng service ─────────────

    [Fact]
    public void vnp_SecureHashType_phai_duoc_LOAI_khoi_phan_bam()
    {
        var thamSo = TaoThamSo();
        thamSo["vnp_SecureHash"] = Ky(thamSo);

        // VNPay các phiên bản cũ gửi kèm khoá này, và nó KHÔNG nằm trong phần được ký.
        // Quên loại thì chuỗi ta dựng dài hơn chuỗi VNPay đã ký đúng một tham số, và
        // triệu chứng là "chữ ký sai" - đủ để đi tìm nhầm ở phép băm hàng giờ.
        thamSo["vnp_SecureHashType"] = "SHA512";

        var ketQua = TaoService().Verify(
            thamSo.ToDictionary(c => c.Key, c => (string?)c.Value, StringComparer.Ordinal));

        Assert.True(ketQua.ChuKyHopLe);
    }

    [Fact]
    public void Chu_ky_viet_HOA_van_duoc_chap_nhan()
    {
        var thamSo = TaoThamSo();
        thamSo["vnp_SecureHash"] = Ky(thamSo).ToUpperInvariant();

        // VNPay không cam kết hoa hay thường. So sánh phân biệt hoa/thường là tự tạo
        // ra một lỗi chỉ xuất hiện khi đối tác đổi cách viết - không báo trước.
        var ketQua = TaoService().Verify(
            thamSo.ToDictionary(c => c.Key, c => (string?)c.Value, StringComparer.Ordinal));

        Assert.True(ketQua.ChuKyHopLe);
    }

    [Fact]
    public void Chu_ky_sai_thi_KHONG_tra_kem_du_lieu_da_doc_duoc()
    {
        var thamSo = TaoThamSo();
        thamSo["vnp_SecureHash"] = new string('c', 128);

        var ketQua = TaoService().Verify(
            thamSo.ToDictionary(c => c.Key, c => (string?)c.Value, StringComparer.Ordinal));

        // Trả kèm OrderId "cho tiện" là mời tầng trên lỡ tay dùng một giá trị chưa
        // được xác thực.
        Assert.Same(VnPayReturn.KhongHopLe, ketQua);
        Assert.Null(ketQua.OrderId);
    }

    [Fact]
    public void So_tien_duoc_chia_lai_100_khi_doc_ve()
    {
        var thamSo = TaoThamSo();
        thamSo["vnp_SecureHash"] = Ky(thamSo);

        var ketQua = TaoService().Verify(
            thamSo.ToDictionary(c => c.Key, c => (string?)c.Value, StringComparer.Ordinal));

        // VNPay trả về số đã nhân 100, đúng như lúc gửi đi.
        Assert.Equal(12_500_000m, ketQua.Amount);
    }

    [Fact]
    public void Ma_00_ma_chu_ky_KHONG_hop_le_thi_van_khong_phai_thanh_cong()
    {
        var gia = new VnPayReturn(
            ChuKyHopLe: false,
            OrderId: 12345,
            ResponseCode: "00",
            TransactionStatus: "00",
            TransactionNo: "1",
            BankCode: "NCB",
            Amount: 1m);

        // ★ Test này sinh ra từ một mutation ĐÃ LỌT LƯỚI: bỏ `ChuKyHopLe &&` khỏi
        // ThanhToanThanhCong thì cả 19 test kia vẫn xanh.
        //
        // Lý do lọt: Verify() trả về KhongHopLe với TOÀN null khi chữ ký sai, nên
        // ResponseCode không bao giờ là "00" cùng lúc với chữ ký sai - qua đường công
        // khai thì nhánh đó không chạm tới được. An toàn thật sự nằm ở Verify().
        //
        // Nhưng lệnh kiểm đó vẫn phải ở lại: ngày nào có người "cải tiến" Verify để
        // trả kèm dữ liệu đã đọc được (rất hợp lý khi muốn ghi log), nó là thứ duy
        // nhất còn đứng giữa một chữ ký giả và chữ "Thanh toán thành công".
        Assert.False(gia.ThanhToanThanhCong);
        Assert.False(gia.KhachTuHuy);
    }

    private static VnPayService TaoService() =>
        new(
            Options.Create(new VnPayOptions
            {
                TmnCode = TmnCode,
                HashSecret = HashSecret,
                BaseUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                ReturnUrl = "http://localhost:5231/Payment/Return"
            }),
            TimeProvider.System);
}
