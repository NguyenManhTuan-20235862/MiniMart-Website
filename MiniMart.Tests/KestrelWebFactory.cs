using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MiniMart.Tests;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> nhưng phục vụ qua <b>Kestrel thật</b>
/// trên một cổng thật, để Playwright có địa chỉ HTTP mà trỏ trình duyệt vào.
///
/// <para>
/// Đây là toàn bộ lý do class này tồn tại. <c>WebApplicationFactory</c> mặc định chạy
/// app trên <c>TestServer</c> — một server trong bộ nhớ, KHÔNG mở socket, KHÔNG có URL.
/// Nó hoàn hảo cho test gửi <c>HttpClient</c> nhưng vô dụng với một trình duyệt thật:
/// không có gì để <c>page.GotoAsync(...)</c> cả.
/// </para>
/// <para>
/// Vì sao không đơn giản là <c>dotnet run</c> ở một tiến trình riêng: mất luôn
/// <c>Services</c>. Mọi bộ test của dự án đều seed và dọn dữ liệu qua
/// <c>factory.Services.CreateScope()</c>; với một tiến trình ngoài thì phải tự mở
/// <c>DbContext</c> thứ hai, tự quản lý vòng đời tiến trình, và tự đoán khi nào app đã
/// sẵn sàng nhận request. Cách dưới đây giữ được cả hai.
/// </para>
/// </summary>
public class KestrelWebFactory : WebApplicationFactory<Program>
{
    private IHost? _kestrel;

    /// <summary>Địa chỉ gốc thật, ví dụ <c>http://127.0.0.1:51234</c>.</summary>
    public string DiaChiGoc { get; private set; } = string.Empty;

    /// <summary>
    /// ★ Thứ tự các bước ở đây KHÔNG thể đảo, và từng bước có lý do riêng.
    ///
    /// <para>
    /// Kết quả cuối cùng là <b>hai</b> host cùng chạy trên cùng cấu hình: một
    /// <c>TestServer</c> để lớp cơ sở và <c>CreateClient()</c> hoạt động như mọi bộ test
    /// khác, và một Kestrel thật để trình duyệt vào được. Chúng dùng chung
    /// <c>appsettings</c> và chung database, nên seed qua host nào cũng thấy ở host kia.
    /// </para>
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // (1) Dựng host TestServer TRƯỚC khi đổi builder sang Kestrel. Đổi rồi mới build
        //     thì không còn cách nào lấy lại bản TestServer, mà lớp cơ sở của
        //     WebApplicationFactory bắt buộc phải nhận đúng kiểu TestServer.
        var testHost = builder.Build();

        // (2) Cổng 0 = để hệ điều hành tự chọn cổng trống. Đóng cứng một cổng là test
        //     đổ khi chạy song song hoặc khi máy đang có thứ khác chiếm cổng đó.
        builder.ConfigureWebHost(web => web.UseKestrel().UseUrls("http://127.0.0.1:0"));

        _kestrel = builder.Build();

        // (3) Phải Start() Kestrel TRƯỚC testHost. Với minimal hosting, host chỉ thật sự
        //     khởi tạo server khi Start, nên đọc địa chỉ trước đó sẽ ra danh sách rỗng.
        _kestrel.Start();

        var server = _kestrel.Services.GetRequiredService<IServer>();
        var diaChi = server.Features.Get<IServerAddressesFeature>()!.Addresses;

        DiaChiGoc = diaChi.Last().TrimEnd('/');

        // (4) Trả về host TestServer, KHÔNG phải Kestrel. Trả nhầm thì lớp cơ sở ném
        //     ngay lúc khởi tạo vì nó ép kiểu server về TestServer.
        testHost.Start();

        return testHost;
    }

    protected override void Dispose(bool disposing)
    {
        // Hai host thì phải dọn cả hai. Bỏ sót Kestrel là cổng bị giữ và tiến trình
        // test không thoát — kiểu treo chỉ thấy khi chạy cả bộ.
        if (disposing)
        {
            _kestrel?.Dispose();
            _kestrel = null;
        }

        base.Dispose(disposing);
    }
}
