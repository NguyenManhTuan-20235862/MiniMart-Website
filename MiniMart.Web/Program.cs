using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using MiniMart.Application;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure;
using MiniMart.Infrastructure.Repositories;
using MiniMart.Web;
using MiniMart.Web.Middleware;
using MiniMart.Web.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────── Logging ───────────────────────────
//
// ClearProviders() TRƯỚC: CreateBuilder đã tự thêm Console/Debug/EventSource. Không xoá
// thì mỗi dòng log hiện HAI lần trên console - một lần của provider mặc định, một lần
// của sink Console của Serilog.
builder.Logging.ClearProviders();

// ★ writeToProviders: true là BẮT BUỘC, và đây là thứ tôi đã đoán sai một lần.
//
// `AddSerilog` trên IServiceCollection KHÔNG chỉ thêm một provider - nó đăng ký luôn
// `SerilogLoggerFactory` làm `ILoggerFactory`, và factory đó MẶC ĐỊNH bỏ qua mọi
// `ILoggerProvider` khác. Tưởng "AddSerilog thì cộng thêm, UseSerilog mới thay thế" là
// sai: cả hai đều thay factory, chỉ khác chỗ đặt.
//
// Hệ quả đo được ở dự án này: ProductBulkUpdateSqlTests gắn một ILoggerProvider riêng để
// bắt câu lệnh SQL và chứng minh bulk update không bị N+1. Thiếu cờ này thì provider đó
// KHÔNG nhận được gì, test đếm 0 lệnh thay vì 2, và thông báo lỗi ("Expected 2, Actual 0")
// không hề gợi ý gì về logging.
builder.Services.AddSerilog(
    writeToProviders: true,

    // ★ preserveStaticLogger: true - KHÔNG đụng vào `Serilog.Log.Logger` tĩnh.
    //
    // Mặc định (false) là mỗi host gán logger của mình vào một biến static TOÀN CỤC và
    // dispose logger trước đó. Với ứng dụng thật chỉ có một host nên vô hại; trong bộ
    // test thì hàng chục WebApplicationFactory chạy SONG SONG, mỗi cái đè lên cái trước
    // và dispose logger mà host khác đang dùng.
    //
    // Đã đo: ProductBulkUpdateSqlTests XANH khi chạy riêng, ĐỎ khi chạy cả bộ (đếm 0
    // lệnh SQL) - đúng hình dạng của một test flaky do hạ tầng, mà nguyên nhân thật là
    // một biến static.
    //
    // Không mất gì: dự án không dùng API tĩnh `Log.Information(...)` ở đâu cả, mọi chỗ
    // đều tiêm `ILogger<T>` qua DI.
    preserveStaticLogger: true,
    configureLogger: (services, loggerConfiguration) => loggerConfiguration
    // Toàn bộ mức log, sink và định dạng đọc từ section "Serilog" của appsettings -
    // KHÔNG hardcode ở đây. Đổi mức log trên máy chủ không nên phải build lại.
    .ReadFrom.Configuration(builder.Configuration)

        // Cho phép sink/enricher lấy dependency từ DI khi cần.
        .ReadFrom.Services(services));

// Add services to the container.
builder.Services.AddControllersWithViews();

// HtmlEncoder mặc định escape MỌI ký tự non-ASCII, nên "Khoảng giá" do Razor
// sinh ra biến thành "Kho&#x1EA3;ng gi&#xE1;". Vẫn đúng về hiển thị nhưng làm
// HTML phình to và không đọc được khi debug hay khi assert trong test.
//
// Mặc định đó tồn tại để chống XSS lợi dụng nhập nhằng bộ ký tự ở các encoding
// cũ. Ta luôn phục vụ UTF-8 (charset=utf-8 trong Content-Type) nên nới ra an
// toàn: UnicodeRanges.All VẪN escape < > & " ' - tức vẫn chặn XSS.
builder.Services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));

// Composition Root: nơi duy nhất được biết class implementation cụ thể.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Lưu trữ file là khái niệm của tầng Web (wwwroot) nên đăng ký tại đây,
// không nằm trong AddApplication/AddInfrastructure.
builder.Services.AddScoped<IProductImageStorage, WebRootProductImageStorage>();

// ─────────────────────── Giỏ hàng: chọn kho lưu trữ ───────────────────────

// Cần cho HttpContextCurrentUser và SessionCartStore. KHÔNG được đăng ký mặc
// định; thiếu dòng này thì cả hai class trên đổ ngay khi có request đầu tiên.
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// SessionCartStore ở tầng Web (phụ thuộc HttpContext.Session) nên đăng ký tại
// đây; DatabaseCartStore đã được AddInfrastructure đăng ký.
builder.Services.AddScoped<SessionCartStore>();

// ★ Factory: câu "if (đã đăng nhập)" của toàn bộ tính năng giỏ hàng nằm ĐÚNG
// MỘT LẦN ở đây. Controller và CartService không hề biết Session hay DB tồn tại.
//
// Scoped, không phải Singleton hay Transient:
//   - Singleton: quyết định bị đóng băng lúc khởi động ứng dụng, và giữ luôn
//     DatabaseCartStore -> kéo theo DbContext (Scoped) thành captive dependency.
//   - Transient: mỗi chỗ inject lại chạy lại factory. Không sai nhưng thừa, và
//     hai chỗ trong cùng request có thể nhận hai instance khác nhau.
//   - Scoped: đúng một quyết định cho mỗi request, khớp với thực tế là trạng
//     thái đăng nhập không đổi giữa chừng một request.
//
// ⚠ Factory chạy LƯỜI - lần đầu có ai đó yêu cầu ICartStore trong request. Với
// Controller thì thời điểm đó luôn nằm SAU UseAuthentication, nên HttpContext.User
// đã có claims và quyết định là đúng. Nếu sau này có middleware nào resolve
// ICartStore TRƯỚC UseAuthentication, nó sẽ nhận SessionCartStore cho cả người
// đã đăng nhập - hỏng im lặng, không exception nào.
builder.Services.AddScoped<ICartStore>(serviceProvider =>
{
    var currentUser = serviceProvider.GetRequiredService<ICurrentUser>();

    return currentUser.IsAuthenticated
        ? serviceProvider.GetRequiredService<DatabaseCartStore>()
        : serviceProvider.GetRequiredService<SessionCartStore>();
});

// Hai lối vào TƯỜNG MINH, bỏ qua factory - chỉ dành cho việc GỘP giỏ sau đăng
// nhập, thao tác duy nhất cần cả hai kho cùng lúc (xem CartStoreKeys).
//
// Uỷ nhiệm sang chính hai registration Scoped ở trên chứ không
// AddKeyedScoped<ICartStore, DatabaseCartStore>(): viết kiểu đó thì keyed và
// non-keyed là hai registration khác nhau nên sinh hai INSTANCE khác nhau trong
// cùng một scope. Ở đây vô hại (cả hai store đều không giữ state riêng, DbContext
// vẫn là một) nhưng "một kho một instance mỗi request" là tính chất đáng giữ.
builder.Services.AddKeyedScoped<ICartStore>(
    CartStoreKeys.Session,
    (serviceProvider, _) => serviceProvider.GetRequiredService<SessionCartStore>());

builder.Services.AddKeyedScoped<ICartStore>(
    CartStoreKeys.Database,
    (serviceProvider, _) => serviceProvider.GetRequiredService<DatabaseCartStore>());

// Session cho giỏ hàng của khách vãng lai.
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "MiniMart.Session";
    options.Cookie.HttpOnly = true;

    // Cookie phiên là ĐIỀU KIỆN CẦN để tính năng hoạt động, không phải cookie
    // theo dõi. Không đánh dấu Essential thì khi bật cookie consent sau này,
    // giỏ hàng của khách chưa đồng ý sẽ im lặng không lưu được gì.
    options.Cookie.IsEssential = true;

    options.IdleTimeout = TimeSpan.FromDays(2);
});

// fetch() không tự gửi __RequestVerificationToken như form HTML, nên token phải
// đi qua header. Bỏ [ValidateAntiForgeryToken] cho tiện là mở đường CSRF.
builder.Services.AddAntiforgery(options =>
    options.HeaderName = "RequestVerificationToken");

// Cookie Authentication: đặt Cookie làm scheme mặc định, nên [Authorize] không
// tham số sẽ dùng scheme này.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // Ba đường dẫn framework tự redirect tới. Controller tương ứng chưa tồn
        // tại - sẽ tạo ở phase sau, khai báo trước cho khớp.
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";

        options.Cookie.Name = "MiniMart.Auth";

        // Chặn JavaScript đọc cookie -> XSS không lấy được phiên đăng nhập.
        options.Cookie.HttpOnly = true;

        // Lax: không gửi cookie khi request đến từ site khác bằng POST,
        // chặn phần lớn kịch bản CSRF mà vẫn giữ được điều hướng bình thường.
        options.Cookie.SameSite = SameSiteMode.Lax;

        // Profile "http" chạy localhost không TLS, ép Always sẽ khiến trình duyệt
        // không bao giờ gửi cookie -> đăng nhập thất bại im lặng khi dev.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        options.ExpireTimeSpan = TimeSpan.FromHours(8);

        // Còn hoạt động thì hạn tự gia hạn; ngồi im quá 8 tiếng mới bị đăng xuất.
        options.SlidingExpiration = true;
    });

// Rate limit cho đăng nhập: chặn brute-force mật khẩu. Không có nó thì kẻ tấn
// công thử được vài nghìn mật khẩu mỗi phút và mọi quy tắc độ mạnh đều vô nghĩa.
builder.Services.AddRateLimiter(options =>
{
    // Mặc định của framework là 503 Service Unavailable - sai nghĩa, vì server
    // vẫn khoẻ. 429 mới là mã cho "bạn gửi quá nhiều".
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    var soLanChoPhep = builder.Configuration.GetValue("RateLimiting:LoginPermitLimit", 5);

    options.AddPolicy(RateLimitPolicies.Login, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Phân vùng theo IP, KHÔNG theo username: phân vùng theo username thì
            // kẻ tấn công chỉ cần đổi username sau mỗi lần thử là thoát giới hạn.
            // Đánh đổi: nhiều người sau cùng một NAT dùng chung hạn mức.
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = soLanChoPhep,
                Window = TimeSpan.FromMinutes(1),

                // QueueLimit = 0: vượt hạn thì TỪ CHỐI ngay, không xếp hàng chờ.
                // Xếp hàng ở đây là tự tạo chỗ cho DoS - mỗi request chờ vẫn giữ
                // một luồng và một kết nối.
                QueueLimit = 0
            }));
});

var app = builder.Build();

// ─────────────────────── Pipeline HTTP ───────────────────────
//
// ★ PHẢI LÀ MIDDLEWARE ĐẦU TIÊN. Pipeline là các lớp lồng nhau: đăng ký sớm nghĩa là
// nằm NGOÀI, mà một khối try/catch chỉ bắt được thứ ném ra từ BÊN TRONG nó. Dời dòng
// này xuống dưới UseAuthentication là mọi exception của UseRouting, UseSession,
// UseRateLimiter và chính UseAuthentication đều thoát ra ngoài nó.
//
// ⚠ Và đây là lý do UseExceptionHandler("/Home/Error") đã bị GỠ chứ không giữ lại cho
// chắc: nó nằm ở đúng vị trí này nên sẽ là lớp TRONG so với middleware của ta, mà lớp
// trong bắt trước. Giữ cả hai thì trong Production - môi trường duy nhất nó quan trọng -
// middleware này KHÔNG BAO GIỜ chạy, còn ở Development thì lại chạy. Tức hành vi xử lý
// lỗi khác nhau giữa hai môi trường, theo chiều xấu nhất: thứ được kiểm bằng test là
// thứ không chạy thật.
app.UseMiddleware<GlobalExceptionMiddleware>();

if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// BẮT BUỘC đứng SAU UseRouting: policy được khai báo bằng attribute trên action,
// mà chỉ sau UseRouting thì endpoint (và metadata của nó) mới được xác định.
// Đặt trước UseRouting thì middleware không thấy [EnableRateLimiting] nào và im
// lặng không giới hạn gì - đã kiểm chứng bằng mutation test: response trả 400
// (antiforgery) thay vì 429, tức rate limit biến mất hoàn toàn.
app.UseRateLimiter();

// Ràng buộc thật: phải đứng TRƯỚC mọi middleware TỰ VIẾT nào đọc
// HttpContext.Session. Thiếu hẳn dòng này thì mọi request giỏ hàng ném
// "Session has not been configured for this application or request".
//
// KHÔNG phải ràng buộc: vị trí so với MapControllerRoute. Với minimal hosting,
// MapControllerRoute chỉ ĐĂNG KÝ endpoint vào bảng route; middleware THỰC THI
// endpoint được framework tự chèn vào CUỐI pipeline lúc build. Nên UseSession()
// viết ở đâu trong khối app.UseX() cũng chạy trước Controller. Đã mutation test:
// dời xuống sát app.Run() thì cả 22 test của CartControllerTests vẫn xanh.
// (Điều này khác mô hình Startup/UseEndpoints cũ, nơi thứ tự có ý nghĩa thật.)
//
// Không phụ thuộc UseAuthentication theo chiều nào - cookie auth không dùng Session.
app.UseSession();

// BẮT BUỘC đứng trước UseAuthorization: middleware này đọc cookie và gán
// HttpContext.User. Thiếu nó thì User luôn rỗng và mọi [Authorize] đều trượt.
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// Route cho Area phải đăng ký TRƯỚC route default, vì route được duyệt theo
// thứ tự: "Admin/Dashboard" khớp {controller}/{action} trước khi kịp tới đây.
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

// Top-level statements sinh ra class Program là internal. Khai báo lại thành
// public để WebApplicationFactory<Program> trong test project nhìn thấy được.
public partial class Program;
