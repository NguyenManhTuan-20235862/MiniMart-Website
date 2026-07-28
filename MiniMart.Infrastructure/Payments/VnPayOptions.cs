namespace MiniMart.Infrastructure.Payments;

/// <summary>
/// Cấu hình cổng thanh toán VNPay (môi trường sandbox).
///
/// <para>
/// Đặt ở <b>Infrastructure</b> vì VNPay là một hệ thống NGOÀI: cùng loại với DbContext
/// và connection string. Application chỉ biết "có một cổng thanh toán", không biết nó
/// tên gì hay ký bằng thuật toán nào - đúng như Application không biết SQL Server tồn tại.
/// </para>
/// <para>
/// Là một <b>class</b> chứ không phải <c>record</c>: <c>IOptions&lt;T&gt;</c> cần
/// constructor không tham số và property có <c>set</c> để binder gán từng khoá cấu hình.
/// Record với positional parameter sẽ bind ra toàn giá trị mặc định - âm thầm, không lỗi.
/// </para>
/// </summary>
public class VnPayOptions
{
    /// <summary>Tên section trong cấu hình. Là hằng để test và Program.cs không gõ lại chuỗi.</summary>
    public const string SectionName = "VnPay";

    /// <summary>
    /// Mã website do VNPay cấp. KHÔNG phải bí mật - nó đi trong query string của URL
    /// thanh toán nên người dùng nhìn thấy được. Vì vậy nó nằm ở <c>appsettings.json</c>.
    /// </summary>
    public string TmnCode { get; set; } = string.Empty;

    /// <summary>
    /// Khoá bí mật dùng để ký (HMAC-SHA512) và để KIỂM chữ ký khi VNPay gọi về.
    ///
    /// <para>
    /// ⚠ TUYỆT ĐỐI không đặt giá trị thật vào <c>appsettings.json</c> hay vào code.
    /// Ai có khoá này thì tự tạo được một chữ ký hợp lệ, tức là tự "báo" cho hệ thống
    /// rằng một đơn hàng đã thanh toán thành công mà không trả đồng nào. Nó không bảo
    /// vệ dữ liệu - nó bảo vệ TIỀN.
    /// </para>
    /// <para>
    /// Nguồn hợp lệ: User Secrets khi dev, biến môi trường <c>VnPay__HashSecret</c> khi
    /// triển khai và trên CI. Cả hai đều nằm ngoài repo.
    /// </para>
    /// </summary>
    public string HashSecret { get; set; } = string.Empty;

    /// <summary>URL sandbox của VNPay để dựng lệnh thanh toán. Công khai, có trong tài liệu.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Nơi VNPay đưa trình duyệt của khách quay về sau khi thanh toán.
    ///
    /// <para>
    /// Là URL TUYỆT ĐỐI vì bên nhận là hệ thống khác, không phải trình duyệt đang ở
    /// trên site ta. Và phải khớp với URL đã khai báo trên cổng quản trị VNPay.
    /// </para>
    /// </summary>
    public string ReturnUrl { get; set; } = string.Empty;
}
