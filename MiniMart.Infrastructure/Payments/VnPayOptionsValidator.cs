using Microsoft.Extensions.Options;

namespace MiniMart.Infrastructure.Payments;

/// <summary>
/// Kiểm tra cấu hình VNPay ngay lúc KHỞI ĐỘNG, không đợi tới lúc có người bấm thanh toán.
///
/// <para>
/// Viết tay <see cref="IValidateOptions{TOptions}"/> thay vì dùng
/// <c>ValidateDataAnnotations()</c>: Data Annotation cho ra thông báo kiểu "The HashSecret
/// field is required" - đúng nhưng vô dụng, vì người đọc nó đang không biết PHẢI LÀM GÌ.
/// Ở đây thông báo nói thẳng câu lệnh cần chạy. Cấu hình sai là lỗi của người vận hành,
/// và thông báo lỗi là toàn bộ giao diện dành cho họ.
/// </para>
/// <para>
/// Đây cũng là lý do chọn fail-fast: thiếu khoá bí mật mà vẫn cho ứng dụng chạy thì lỗi
/// nổ ra ở giữa một lần thanh toán thật - đúng lúc có tiền của khách đang đi qua.
/// </para>
/// </summary>
public class VnPayOptionsValidator : IValidateOptions<VnPayOptions>
{
    public ValidateOptionsResult Validate(string? name, VnPayOptions options)
    {
        var loi = new List<string>();

        if (string.IsNullOrWhiteSpace(options.TmnCode))
        {
            loi.Add(
                "Thiếu 'VnPay:TmnCode'. Đây KHÔNG phải bí mật (nó đi trong URL thanh toán) " +
                "nhưng vẫn phải khai báo. Lấy ở cổng quản trị sandbox của VNPay.");
        }

        if (string.IsNullOrWhiteSpace(options.HashSecret))
        {
            loi.Add(
                "Thiếu 'VnPay:HashSecret'. TUYỆT ĐỐI không ghi vào appsettings.json. " +
                "Khi dev, chạy: dotnet user-secrets set \"VnPay:HashSecret\" \"<khoa-sandbox>\" " +
                "--project MiniMart.Web. Khi triển khai/CI, đặt biến môi trường VnPay__HashSecret.");
        }

        // Kiểm ĐỊNH DẠNG chứ không chỉ kiểm rỗng: cấu hình sai kiểu "quên https://"
        // vẫn qua được lệnh kiểm rỗng, rồi hỏng ở tận lúc ghép URL gửi sang VNPay.
        foreach (var (khoa, giaTri) in new[]
        {
            ("VnPay:BaseUrl", options.BaseUrl),
            ("VnPay:ReturnUrl", options.ReturnUrl)
        })
        {
            if (string.IsNullOrWhiteSpace(giaTri))
            {
                loi.Add($"Thiếu '{khoa}'.");
            }
            // ★ Phải kiểm CẢ SCHEME, không chỉ "có phải URI tuyệt đối không".
            //
            // `Uri.TryCreate(x, UriKind.Absolute, ...)` cho kết quả KHÁC NHAU theo hệ
            // điều hành: với "/Payment/Return" nó trả false trên Windows nhưng TRUE trên
            // Linux, vì Unix coi đường dẫn bắt đầu bằng '/' là một file URI tuyệt đối
            // (file:///Payment/Return). Lệnh kiểm cũ vì vậy vô hiệu trên chính môi
            // trường triển khai, trong khi vẫn xanh trên máy dev Windows.
            //
            // Đây là bug do CI tìm ra ở lần chạy thứ hai của dự án - loại lỗi mà không
            // lần thử tay nào trên Windows chạm tới được.
            else if (!Uri.TryCreate(giaTri, UriKind.Absolute, out var uri)
                     || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                loi.Add(
                    $"'{khoa}' phải là URL TUYỆT ĐỐI dùng http:// hoặc https://, " +
                    $"đang là '{giaTri}'. VNPay là hệ thống khác nên đường dẫn tương đối vô nghĩa.");
            }
        }

        return loi.Count == 0
            // Gộp TẤT CẢ lỗi vào một lần báo, không return ngay ở lỗi đầu tiên: người
            // cấu hình sửa được cả ba trong một lượt thay vì chạy lại ba lần.
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(loi);
    }
}
