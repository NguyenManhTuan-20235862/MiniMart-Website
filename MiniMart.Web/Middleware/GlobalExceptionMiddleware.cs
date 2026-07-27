using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace MiniMart.Web.Middleware;

/// <summary>
/// Lưới cuối cùng: bắt mọi exception CHƯA được xử lý, ghi log, và trả về một response
/// có hình dạng đúng với loại client đang gọi.
///
/// <para>
/// <b>Phạm vi cố ý HẸP.</b> Nó KHÔNG dịch các exception nghiệp vụ
/// (<c>NotFoundException</c>, <c>ConcurrencyConflictException</c>, …) sang mã HTTP.
/// Việc đó đã thuộc về Controller, nơi duy nhất có đủ ngữ cảnh để chọn giữa "render lại
/// form kèm giá trị người dùng vừa nhập" và "trả 404". Thêm một bảng ánh xạ ở đây là
/// tạo ra chỗ thứ hai cùng quyết định một việc, và chỗ nào thắng thì phụ thuộc vào việc
/// Controller có nhớ bắt hay không - tức hành vi của ứng dụng phụ thuộc một điều dễ quên.
/// Tới được middleware này nghĩa là <b>đã có bug</b>, và câu trả lời đúng cho bug là 500.
/// </para>
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Khách đóng tab / mất mạng giữa chừng. CancellationToken được kích hoạt và
            // EF Core ném từ giữa một truy vấn - trông y hệt một sự cố nhưng KHÔNG phải:
            // không có gì hỏng, và cũng không còn ai để trả lời.
            //
            // Ghi ở mức Information chứ không Error. Ghi Error thì mỗi lần có người bấm
            // Stop là một dòng đỏ trong log, và sự cố THẬT chìm giữa đám nhiễu đó.
            _logger.LogInformation(
                "Request {Method} {Path} bị huỷ vì client ngắt kết nối.",
                context.Request.Method, context.Request.Path);
        }
        catch (Exception ex)
        {
            var maTruyVet = Activity.Current?.Id ?? context.TraceIdentifier;

            // Log ĐẦY ĐỦ, luôn luôn, kể cả Production - đây là bản duy nhất còn giữ
            // stack trace sau khi response đã được làm sạch. Kèm mã truy vết để nối
            // được dòng log này với đúng cái màn hình mà người dùng đang nhìn.
            _logger.LogError(ex,
                "Lỗi chưa xử lý ở {Method} {Path}. Mã truy vết {MaTruyVet}.",
                context.Request.Method, context.Request.Path, maTruyVet);

            await GhiPhanHoiAsync(context, maTruyVet, ex);
        }
    }

    private async Task GhiPhanHoiAsync(HttpContext context, string maTruyVet, Exception ex)
    {
        // ★ Không thể sửa response đã bắt đầu gửi đi. Status code và header đã nằm trên
        // đường truyền, nên gán lại chỉ ném InvalidOperationException NGAY TRONG khối
        // catch - biến một lỗi thành hai, và lỗi thứ hai che mất lỗi thứ nhất.
        //
        // Việc đúng là ném lại: Kestrel sẽ ngắt kết nối, client nhận một response cụt.
        // Xấu, nhưng TRUNG THỰC - dán một trang lỗi vào giữa thân response dở dang thì
        // client nhận HTML hợp lệ một nửa và không biết là mình đang đọc rác.
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Response đã bắt đầu gửi nên không thể thay bằng trang lỗi. Mã truy vết {MaTruyVet}.",
                maTruyVet);

            throw ex;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        if (MuonJson(context))
        {
            await GhiJsonAsync(context, maTruyVet, ex);
        }
        else
        {
            await GhiHtmlAsync(context, maTruyVet, ex);
        }
    }

    /// <summary>
    /// Hai tín hiệu, và cần cả hai.
    ///
    /// <para>
    /// <c>Accept</c> phục vụ client trình duyệt gọi <c>fetch</c>. Attribute phục vụ
    /// client server-to-server như IPN của VNPay - loại không cam kết gửi <c>Accept</c>
    /// nào, mà đoán sai thì một chương trình nhận được trang HTML tiếng Việt.
    /// </para>
    /// <para>
    /// Dùng <c>Contains</c> chứ không so bằng: trình duyệt gửi cả một danh sách kèm
    /// q-value (<c>application/json, text/plain, */*;q=0.8</c>) nên so bằng luôn trượt.
    /// Cùng quy ước với các endpoint giỏ hàng.
    /// </para>
    /// <para>
    /// ⚠ <c>GetEndpoint()</c> đọc được ở đây dù middleware này đăng ký TRƯỚC
    /// <c>UseRouting</c>: thứ tự đăng ký quyết định thứ tự đi VÀO, còn khối
    /// <c>catch</c> chạy trên đường đi RA - lúc đó routing đã khớp xong từ lâu.
    /// </para>
    /// </summary>
    private static bool MuonJson(HttpContext context)
    {
        if (context.Request.Headers.Accept.ToString()
            .Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return context.GetEndpoint()?.Metadata.GetMetadata<JsonErrorResponseAttribute>() is not null;
    }

    private async Task GhiJsonAsync(HttpContext context, string maTruyVet, Exception ex)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        // Hình dạng cố định để client parse được: luôn có `error` và `traceId`, và chỉ
        // Development mới có thêm `detail`.
        var than = new Dictionary<string, string>
        {
            ["error"] = "Đã xảy ra lỗi khi xử lý yêu cầu.",
            ["traceId"] = maTruyVet
        };

        if (_environment.IsDevelopment())
        {
            than["detail"] = ex.ToString();
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(than), context.RequestAborted);
    }

    /// <summary>
    /// Trang lỗi tự chứa, KHÔNG đi qua Razor và KHÔNG dùng <c>_Layout</c>.
    ///
    /// <para>
    /// Cố ý: thứ vừa ném có thể chính là <c>_Layout</c>, một view component, hoặc một
    /// service mà layout cần. Render trang lỗi qua MVC khi đó là ném lần thứ hai ngay
    /// bên trong trình xử lý lỗi - và lần thứ hai thì không còn ai bắt. Trang này chỉ
    /// phụ thuộc vào việc ghi được chuỗi vào response.
    /// </para>
    /// <para>
    /// Đánh đổi đã biết: nó không mang giao diện chung của site. Chấp nhận được cho một
    /// trang mà mục tiêu duy nhất là "nói thật, và luôn hiện ra được".
    /// </para>
    /// </summary>
    private async Task GhiHtmlAsync(HttpContext context, string maTruyVet, Exception ex)
    {
        context.Response.ContentType = "text/html; charset=utf-8";

        // HtmlEncode cả mã truy vết lẫn nội dung exception. Mã truy vết do server sinh
        // nên hôm nay an toàn, nhưng thông điệp exception thì THƯỜNG chứa dữ liệu người
        // dùng nhập (tên sản phẩm, chuỗi truy vấn) - ghép thẳng vào HTML là mở XSS ở
        // đúng trang mà không ai nghĩ tới việc kiểm.
        var chiTiet = _environment.IsDevelopment()
            ? $"""
               <h2>Chi tiết (chỉ hiện ở Development)</h2>
               <pre>{WebUtility.HtmlEncode(ex.ToString())}</pre>
               """
            : string.Empty;

        // $$""" với chỗ nội suy {{...}}: CSS có dấu ngoặc nhọn, mà với $""" thì mỗi `{`
        // là một chỗ nội suy nên file không biên dịch được.
        var html = $$"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>Đã xảy ra lỗi - MiniMart</title>
                <style>
                    body { font-family: system-ui, sans-serif; margin: 3rem auto; max-width: 40rem; padding: 0 1rem; }
                    code { background: #f1f1f1; padding: .15rem .4rem; border-radius: .2rem; }
                    pre { background: #f1f1f1; padding: 1rem; overflow-x: auto; font-size: .85rem; }
                    a { color: #0d6efd; }
                </style>
            </head>
            <body>
                <h1>Đã xảy ra lỗi</h1>
                <p>
                    Rất tiếc, hệ thống gặp sự cố khi xử lý yêu cầu của bạn. Lỗi đã được ghi nhận
                    và chúng tôi sẽ xem xét.
                </p>
                <p>
                    Nếu bạn cần liên hệ hỗ trợ, vui lòng cung cấp mã truy vết sau:<br />
                    <code>{{WebUtility.HtmlEncode(maTruyVet)}}</code>
                </p>
                <p><a href="/">Quay về trang chủ</a></p>
                {{chiTiet}}
            </body>
            </html>
            """;

        await context.Response.WriteAsync(html, context.RequestAborted);
    }
}
