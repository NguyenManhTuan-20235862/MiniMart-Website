using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Domain.ValueObjects;

namespace MiniMart.Infrastructure.Payments;

/// <summary>
/// Dựng URL thanh toán VNPay và ký nó bằng HMAC-SHA512.
///
/// <para>
/// Toàn bộ giá trị của class này nằm ở việc tạo ra CHÍNH XÁC chuỗi mà máy chủ VNPay
/// sẽ dựng lại ở phía họ. Họ nhận request, tự ghép lại chuỗi theo cùng quy tắc, tự
/// băm bằng cùng khoá, rồi so với chữ ký ta gửi. Lệch một ký tự - một dấu cách mã
/// hoá khác kiểu, một tham số sai vị trí - là ra một chữ ký hoàn toàn khác và VNPay
/// từ chối cả request. Không có "gần đúng" ở đây.
/// </para>
/// </summary>
public class VnPayService : IVnPayService
{
    /// <summary>
    /// Giờ Việt Nam, đóng cứng thành offset +7 thay vì tra <c>TimeZoneInfo</c>.
    ///
    /// <para>
    /// Hai lý do. Một: tên múi giờ khác nhau giữa Windows ("SE Asia Standard Time")
    /// và Linux ("Asia/Ho_Chi_Minh"), nên tra theo tên là code chạy ở máy dev và đổ
    /// trên CI. Hai: Việt Nam không có giờ mùa hè từ 1975, nên offset là hằng số thật
    /// chứ không phải một phép đơn giản hoá.
    /// </para>
    /// </summary>
    private static readonly TimeSpan GioVietNam = TimeSpan.FromHours(7);

    private const string TenChuKy = "vnp_SecureHash";
    private const string TenLoaiChuKy = "vnp_SecureHashType";

    private readonly VnPayOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <param name="timeProvider">
    /// Đồng hồ tiêm từ ngoài, không gọi <c>DateTime.Now</c> trực tiếp. Lý do rất cụ
    /// thể: <c>vnp_CreateDate</c> đi vào chữ ký, nên gọi đồng hồ thật là chữ ký đổi
    /// theo từng giây và không test được bằng giá trị mong đợi cố định.
    /// </param>
    public VnPayService(IOptions<VnPayOptions> options, TimeProvider timeProvider)
    {
        // .Value đọc MỘT lần trong constructor. Dùng IOptions (không phải
        // IOptionsMonitor) vì cấu hình cổng thanh toán không được đổi giữa chừng:
        // đổi khoá lúc đang có giao dịch dở là tạo ra chữ ký không ai kiểm được.
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public string CreatePaymentUrl(Order order, string clientIpAddress)
    {
        ArgumentNullException.ThrowIfNull(order);

        var bayGio = _timeProvider.GetUtcNow().ToOffset(GioVietNam);

        // ───── BƯỚC 1: gom tham số ─────
        //
        // SortedDictionary với StringComparer.Ordinal: sắp xếp xảy ra NGAY khi thêm,
        // nên không tồn tại trạng thái "đã có đủ tham số nhưng chưa sắp". Dùng
        // Dictionary thường rồi .OrderBy() ở dưới cũng đúng, nhưng khi đó thứ tự là
        // một BƯỚC có thể quên; ở đây nó là TÍNH CHẤT của kiểu dữ liệu.
        var thamSo = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = _options.TmnCode,

            // Nhân 100 và ép sang SỐ NGUYÊN. VNPay nhận số tiền tính bằng đơn vị nhỏ
            // nhất (VND x 100) và KHÔNG chấp nhận dấu thập phân. Ép long chứ không
            // để decimal: decimal.ToString() trên máy vi-VN in ra "1000000,00" -
            // dấu phẩy đi thẳng vào chữ ký và VNPay từ chối, còn máy dev en-US thì
            // không tái hiện được lỗi.
            ["vnp_Amount"] = ((long)(order.TotalAmount * 100m))
                .ToString(CultureInfo.InvariantCulture),

            ["vnp_CurrCode"] = "VND",

            // Chỉ ASCII, không dấu: chuỗi này hiển thị trên nhiều màn hình của VNPay
            // và của ngân hàng, nơi không phải chỗ nào cũng xử lý UTF-8 đúng.
            ["vnp_OrderInfo"] = $"Thanh toan don hang {order.Id}",

            ["vnp_OrderType"] = "other",
            ["vnp_Locale"] = "vn",
            ["vnp_ReturnUrl"] = _options.ReturnUrl,
            ["vnp_IpAddr"] = clientIpAddress,

            // yyyyMMddHHmmss theo GIỜ VIỆT NAM, không phải UTC. Gửi giờ UTC thì lệch
            // 7 tiếng: VNPay coi lệnh là đã hết hạn hoặc đến từ tương lai và từ chối.
            ["vnp_CreateDate"] = bayGio.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            ["vnp_ExpireDate"] = bayGio.AddMinutes(15)
                .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),

            // Mã giao dịch phía ta. Dùng OrderId vì nó đã unique và tra ngược được
            // khi VNPay gọi về.
            //
            // ⚠ Hạn chế đã biết: VNPay từ chối một vnp_TxnRef đã dùng, nên khách bỏ
            // dở rồi thanh toán lại CÙNG đơn sẽ bị từ chối. Sửa đúng là thêm bảng
            // PaymentAttempt và lấy Id của lần thử làm TxnRef - làm khi có luồng
            // "thanh toán lại", không phải bây giờ.
            ["vnp_TxnRef"] = order.Id.ToString(CultureInfo.InvariantCulture)
        };

        // ───── BƯỚC 2 + 3: dựng chuỗi để ký, và ký ─────
        var duLieuKy = GhepChuoi(thamSo);
        var chuKy = Ky(duLieuKy, _options.HashSecret);

        // ───── BƯỚC 4: URL cuối cùng ─────
        //
        // vnp_SecureHash nối vào SAU, và cố ý KHÔNG nằm trong SortedDictionary ở trên:
        // nó là KẾT QUẢ của phép băm nên không thể là đầu vào của chính nó. Lỡ thêm
        // vào là VNPay tính lại hash trên một tập tham số khác -> luôn sai chữ ký.
        return $"{_options.BaseUrl}?{duLieuKy}&{TenChuKy}={chuKy}";
    }

    public VnPayReturn Verify(IReadOnlyDictionary<string, string?> thamSo)
    {
        ArgumentNullException.ThrowIfNull(thamSo);

        if (!thamSo.TryGetValue(TenChuKy, out var chuKyNhanDuoc)
            || string.IsNullOrWhiteSpace(chuKyNhanDuoc))
        {
            return VnPayReturn.KhongHopLe;
        }

        // Dựng lại tập tham số để băm, BỎ hai khoá liên quan tới chính chữ ký.
        //
        // vnp_SecureHashType phải loại cùng với vnp_SecureHash: các phiên bản cũ có
        // gửi kèm nó, và nó KHÔNG nằm trong phần được ký. Quên loại thì chuỗi ta dựng
        // dài hơn chuỗi VNPay đã ký một tham số -> chữ ký không bao giờ khớp, mà triệu
        // chứng lại là "chữ ký sai" nên rất dễ đi tìm nhầm ở phép băm.
        var deKy = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (khoa, giaTri) in thamSo)
        {
            if (khoa.Equals(TenChuKy, StringComparison.Ordinal)
                || khoa.Equals(TenLoaiChuKy, StringComparison.Ordinal))
            {
                continue;
            }

            deKy[khoa] = giaTri ?? string.Empty;
        }

        // ★ Dùng LẠI đúng GhepChuoi và Ky của đường tạo URL, không viết bản kiểm riêng.
        //
        // Đây là ràng buộc quan trọng nhất của cả hàm. Hai đoạn code song song cho hai
        // chiều là hai đoạn sẽ lệch nhau: sửa cách mã hoá ở chiều gửi mà quên chiều
        // nhận thì hệ thống vẫn thanh toán được, chỉ âm thầm từ chối MỌI callback.
        var chuKyTinhLai = Ky(GhepChuoi(deKy), _options.HashSecret);

        if (!SoSanhChuKy(chuKyTinhLai, chuKyNhanDuoc))
        {
            // Trả về "không hợp lệ" mà KHÔNG kèm dữ liệu đã đọc được: chữ ký sai nghĩa
            // là không có gì trong đó đáng tin, kể cả OrderId. Trả kèm là mời người
            // dùng phía sau lỡ tay dùng tới.
            return VnPayReturn.KhongHopLe;
        }

        return new VnPayReturn(
            ChuKyHopLe: true,
            OrderId: DocSoNguyen(thamSo, "vnp_TxnRef"),
            ResponseCode: Doc(thamSo, "vnp_ResponseCode"),
            TransactionStatus: Doc(thamSo, "vnp_TransactionStatus"),
            TransactionNo: Doc(thamSo, "vnp_TransactionNo"),
            BankCode: Doc(thamSo, "vnp_BankCode"),

            // VNPay trả về số tiền đã nhân 100, đúng như lúc gửi đi - chia lại để ra
            // số tiền thật.
            Amount: DocSoNguyen(thamSo, "vnp_Amount") is int x ? x / 100m : null);
    }

    /// <summary>
    /// So sánh hai chữ ký theo THỜI GIAN CỐ ĐỊNH, không dùng <c>==</c>.
    ///
    /// <para>
    /// So sánh chuỗi thông thường dừng ngay ở byte đầu tiên khác nhau, nên thời gian
    /// trả lời rò rỉ việc "đoán đúng được bao nhiêu ký tự đầu". Kẻ tấn công đo hàng
    /// nghìn lần rồi dò dần từng ký tự của chữ ký hợp lệ.
    /// </para>
    /// <para>
    /// Cùng loại lỗ hổng với việc <c>AuthenticateAsync</c> phải băm một mật khẩu giả
    /// khi username không tồn tại (xem <c>rules/auth.md</c>): ở cả hai chỗ, <b>thời
    /// gian</b> là một kênh rò rỉ thông tin.
    /// </para>
    /// </summary>
    private static bool SoSanhChuKy(string tinhLai, string nhanDuoc)
    {
        // VNPay không cam kết hoa hay thường nên chuẩn hoá trước. Bước này nằm NGOÀI
        // phép so thời-gian-cố-định và không sao: độ dài và cách viết hoa không phải
        // bí mật, chỉ NỘI DUNG mới là.
        var a = Encoding.UTF8.GetBytes(tinhLai.ToLowerInvariant());
        var b = Encoding.UTF8.GetBytes(nhanDuoc.Trim().ToLowerInvariant());

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string? Doc(IReadOnlyDictionary<string, string?> thamSo, string khoa) =>
        thamSo.TryGetValue(khoa, out var giaTri) ? giaTri : null;

    private static int? DocSoNguyen(IReadOnlyDictionary<string, string?> thamSo, string khoa) =>
        int.TryParse(Doc(thamSo, khoa), NumberStyles.Integer, CultureInfo.InvariantCulture, out var so)
            ? so
            : null;

    /// <summary>
    /// Ghép <c>key=value&amp;key=value</c> theo đúng thứ tự đã sắp, với value ĐÃ mã hoá URL.
    ///
    /// <para>
    /// ★ Đây là hàm dùng cho CẢ chuỗi-để-ký lẫn query string thật, và đó là điều quan
    /// trọng nhất của cả class. Viết hai đoạn ghép riêng cho hai mục đích là nguyên
    /// nhân số một của lỗi "sai chữ ký": chỉ cần một bên mã hoá dấu cách thành
    /// <c>%20</c> còn bên kia thành <c>+</c> là hai chuỗi khác nhau, trong khi mắt
    /// thường đọc vẫn thấy giống hệt.
    /// </para>
    /// <para>
    /// Dùng <see cref="WebUtility.UrlEncode"/> theo đúng mẫu chính thức của VNPay.
    /// KHÔNG đổi sang <c>Uri.EscapeDataString</c> cho "chuẩn hơn": hai hàm này mã hoá
    /// dấu cách khác nhau, và bên kiểm chữ ký là máy chủ VNPay chứ không phải ta.
    /// </para>
    /// </summary>
    private static string GhepChuoi(SortedDictionary<string, string> thamSo)
    {
        var chuoi = new StringBuilder();

        foreach (var (khoa, giaTri) in thamSo)
        {
            // Bỏ qua tham số rỗng: VNPay không muốn nhận khoá không có giá trị, và
            // gửi "vnp_X=" vẫn được tính vào chữ ký nên hai bên dễ lệch nhau.
            if (string.IsNullOrEmpty(giaTri))
            {
                continue;
            }

            if (chuoi.Length > 0)
            {
                chuoi.Append('&');
            }

            // Chỉ mã hoá GIÁ TRỊ, không mã hoá KHOÁ: khoá toàn ASCII an toàn, và mã
            // hoá khoá sẽ làm hỏng cả thứ tự lẫn cách VNPay tách tham số.
            chuoi.Append(khoa).Append('=').Append(WebUtility.UrlEncode(giaTri));
        }

        return chuoi.ToString();
    }

    /// <summary>
    /// HMAC-SHA512 của <paramref name="duLieu"/> với khoá bí mật, in ra hex.
    ///
    /// <para>
    /// HMAC chứ không phải SHA512 thường: băm thường ai cũng tính được nên không
    /// chứng minh được ai gửi. HMAC trộn KHOÁ BÍ MẬT vào phép băm, nên chỉ hai bên
    /// biết khoá mới tạo ra được chữ ký hợp lệ. Đó cũng là lý do khoá này không được
    /// nằm trong repo - xem <c>.claude/rules/build.md</c>.
    /// </para>
    /// </summary>
    private static string Ky(string duLieu, string khoaBiMat)
    {
        // UTF8 cho cả khoá lẫn dữ liệu. Đổi sang ASCII/Unicode là ra byte khác và
        // chữ ký khác - lỗi im lặng vì mọi thứ vẫn chạy, chỉ VNPay từ chối.
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(khoaBiMat));

        var bam = hmac.ComputeHash(Encoding.UTF8.GetBytes(duLieu));

        // Hex thường. VNPay so sánh không phân biệt hoa/thường ở đầu họ, nhưng cố
        // định một dạng để test so bằng được.
        return Convert.ToHexStringLower(bam);
    }
}
