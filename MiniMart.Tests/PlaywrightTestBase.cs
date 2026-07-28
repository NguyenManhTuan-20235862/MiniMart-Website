using Microsoft.Playwright;

namespace MiniMart.Tests;

/// <summary>
/// Nền chung cho mọi test chạy trong TRÌNH DUYỆT THẬT.
///
/// <para>
/// Lý do tồn tại: 591 test còn lại của dự án gửi <c>HttpClient</c> và đọc chuỗi HTML —
/// <b>không một dòng JavaScript nào được chạy</b>. Nên tất cả những gì
/// <c>home-load-more.js</c> và <c>cart-dropdown.js</c> làm đều chưa từng được kiểm:
/// <c>fetch</c> có gọi đúng URL không, <c>insertAdjacentHTML</c> có dán đúng chỗ không,
/// cờ chặn double-click có tác dụng không. Đổi <c>'beforeend'</c> thành
/// <c>'afterend'</c> làm vỡ lưới sản phẩm mà 591 test vẫn xanh.
/// </para>
/// <para>
/// Ranh giới cần nhớ: các test ở đây kiểm <b>hành vi trong trình duyệt</b>. Chúng KHÔNG
/// thay thế integration test — hình dạng HTML, header, và hợp đồng JSON vẫn thuộc về
/// những bộ test rẻ hơn và tất định hơn.
/// </para>
/// </summary>
public abstract class PlaywrightTestBase : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;

    protected KestrelWebFactory Factory { get; } = new();

    protected IPage Page { get; private set; } = null!;

    /// <summary>Nơi lớp con seed dữ liệu của mình. Chạy TRƯỚC khi trình duyệt mở.</summary>
    protected virtual Task SeedAsync() => Task.CompletedTask;

    /// <summary>Nơi lớp con dọn dữ liệu của mình.</summary>
    protected virtual Task DonDepAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        // Chạm vào Services để ép WebApplicationFactory dựng host ngay bây giờ — đó là
        // lúc KestrelWebFactory.CreateHost chạy và DiaChiGoc mới có giá trị. Không có
        // dòng này thì BaseURL bên dưới là chuỗi rỗng và mọi lệnh GotoAsync đều đổ.
        _ = Factory.Services;

        await SeedAsync();

        _playwright = await Playwright.CreateAsync();

        try
        {
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                // Headless: không mở cửa sổ. Đặt PWDEBUG=1 để xem tận mắt khi cần gỡ lỗi.
                Headless = Environment.GetEnvironmentVariable("PWDEBUG") != "1"
            });
        }
        catch (PlaywrightException ex)
        {
            // ★ Thông báo phải nêu ĐÚNG CÂU LỆNH cần chạy, không chỉ nêu cái gì thiếu.
            //
            // Cùng quy ước với VnPayOptionsValidator: người đọc thông báo này đang
            // KHÔNG biết phải làm gì, và "Executable doesn't exist" của Playwright thì
            // không nói cho họ biết. Đây là lỗi mà mọi máy mới đều gặp đúng một lần.
            throw new InvalidOperationException(
                "Chưa tải trình duyệt cho Playwright. Chạy MỘT lần:\n"
                + "    pwsh MiniMart.Tests/bin/Debug/net10.0/playwright.ps1 install chromium\n"
                + "(trên CI dùng thêm --with-deps để cài cả thư viện hệ thống).",
                ex);
        }

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            // BaseURL cho phép viết GotoAsync("/") thay vì ghép chuỗi cổng ở mọi test.
            BaseURL = Factory.DiaChiGoc,

            // Kích thước cố định: layout Bootstrap đổi theo breakpoint, nên để trình
            // duyệt tự chọn là mở đường cho test đỏ tuỳ máy. 1280 nằm trong khoảng lg.
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });

        Page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        // Dọn theo thứ tự ngược lại thứ tự tạo. Bỏ sót một tầng là tiến trình
        // chromium ở lại sau khi test kết thúc.
        if (_context is not null)
        {
            await _context.CloseAsync();
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();

        await DonDepAsync();

        Factory.Dispose();
    }
}
