using Microsoft.AspNetCore.Authentication.Cookies;
using MiniMart.Application;
using MiniMart.Infrastructure;
using MiniMart.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Composition Root: nơi duy nhất được biết class implementation cụ thể.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Lưu trữ file là khái niệm của tầng Web (wwwroot) nên đăng ký tại đây,
// không nằm trong AddApplication/AddInfrastructure.
builder.Services.AddScoped<IProductImageStorage, WebRootProductImageStorage>();

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

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
