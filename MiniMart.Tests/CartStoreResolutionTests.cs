using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure.Repositories;
using MiniMart.Web;
using MiniMart.Web.Services;

namespace MiniMart.Tests;

/// <summary>
/// Factory chọn kho giỏ hàng trong Program.cs.
///
/// <para>
/// Đây là loại lỗi KHÔNG có exception nào báo: chọn nhầm kho thì giỏ hàng vẫn
/// chạy trơn tru, chỉ là người đã đăng nhập thao tác trên giỏ Session (mất khi
/// restart, không đồng bộ giữa thiết bị) hoặc khách vãng lai đâm vào
/// DatabaseCartStore. Không có test thì chỉ người dùng thật mới phát hiện ra.
/// </para>
/// </summary>
public class CartStoreResolutionTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();

    /// <summary>
    /// Gắn một HttpContext giả vào IHttpContextAccessor rồi resolve ICartStore.
    ///
    /// HttpContextAccessor giữ context trong AsyncLocal, nên phải gán và đọc
    /// trong CÙNG một luồng async - đó là lý do mọi thứ nằm gọn trong một method
    /// thay vì tách sang InitializeAsync.
    /// </summary>
    private (ICartStore Store, IServiceScope Scope) ResolveVoiNguoiDung(int? userId)
    {
        var scope = _factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        var identity = userId is null
            // Không truyền authenticationType -> IsAuthenticated = false, đúng
            // trạng thái khách vãng lai.
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Cookies");

        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = scope.ServiceProvider
        };

        return (scope.ServiceProvider.GetRequiredService<ICartStore>(), scope);
    }

    [Fact]
    public void Khach_vang_lai_nhan_SessionCartStore()
    {
        var (store, scope) = ResolveVoiNguoiDung(null);
        using (scope)
        {
            Assert.IsType<SessionCartStore>(store);
        }
    }

    [Fact]
    public void Nguoi_da_dang_nhap_nhan_DatabaseCartStore()
    {
        var (store, scope) = ResolveVoiNguoiDung(42);
        using (scope)
        {
            Assert.IsType<DatabaseCartStore>(store);
        }
    }

    [Fact]
    public void Cung_mot_request_thi_chi_co_MOT_instance()
    {
        var scope = _factory.Services.CreateScope();
        using (scope)
        {
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

            var lanMot = scope.ServiceProvider.GetRequiredService<ICartStore>();
            var lanHai = scope.ServiceProvider.GetRequiredService<ICartStore>();

            // Scoped chứ không Transient: hai chỗ inject trong cùng request phải
            // thấy cùng một giỏ. Transient thì DatabaseCartStore tạo lại nhiều
            // lần trong một request - không sai kết quả nhưng thừa và khó suy luận.
            Assert.Same(lanMot, lanHai);
        }
    }

    [Fact]
    public void Quyet_dinh_duoc_lay_LAI_cho_moi_request()
    {
        var (khach, scopeKhach) = ResolveVoiNguoiDung(null);
        using (scopeKhach)
        {
            Assert.IsType<SessionCartStore>(khach);
        }

        var (thanhVien, scopeThanhVien) = ResolveVoiNguoiDung(7);
        using (scopeThanhVien)
        {
            // Singleton sẽ đóng băng quyết định của request ĐẦU TIÊN cho toàn bộ
            // vòng đời ứng dụng: ai vào trang trước sẽ quyết định kho lưu trữ của
            // tất cả những người sau.
            Assert.IsType<DatabaseCartStore>(thanhVien);
        }
    }

    [Fact]
    public void ICurrentUser_doc_dung_ClaimTypes_NameIdentifier()
    {
        var scope = _factory.Services.CreateScope();
        using (scope)
        {
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "123")], "Cookies")),
                RequestServices = scope.ServiceProvider
            };

            var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

            // Dùng chuỗi tự chế thay ClaimTypes.NameIdentifier ở một trong hai
            // nơi (đây và AccountController) thì Id luôn null, factory luôn chọn
            // SessionCartStore, và người đã đăng nhập mất giỏ sau mỗi lần restart.
            Assert.Equal(123, currentUser.Id);
            Assert.True(currentUser.IsAuthenticated);
        }
    }

    // ───────────── Hai lối vào tường minh (Keyed DI) ─────────────

    [Fact]
    public void Keyed_DI_tra_ve_dung_kho_theo_tung_khoa()
    {
        using var scope = _factory.Services.CreateScope();

        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        // Đảo hai khoá trong Program.cs là gộp giỏ chạy NGƯỢC: hàng dưới DB bị đẩy
        // sang Session rồi giỏ DB bị xoá sạch. Không exception nào, chỉ là người
        // dùng mất hàng - đúng loại lỗi phải khoá bằng test.
        Assert.IsType<SessionCartStore>(
            scope.ServiceProvider.GetRequiredKeyedService<ICartStore>(CartStoreKeys.Session));

        Assert.IsType<DatabaseCartStore>(
            scope.ServiceProvider.GetRequiredKeyedService<ICartStore>(CartStoreKeys.Database));
    }

    [Fact]
    public void Keyed_va_khong_keyed_dung_CUNG_mot_instance_trong_mot_request()
    {
        var (store, scope) = ResolveVoiNguoiDung(null);
        using (scope)
        {
            // Đăng ký keyed bằng AddKeyedScoped<ICartStore, SessionCartStore>() sẽ
            // là một registration RIÊNG nên sinh instance thứ hai trong cùng scope.
            // Ở đây vô hại (store không giữ state) nhưng "một kho một instance mỗi
            // request" là tính chất đáng giữ, nên uỷ nhiệm sang registration cũ.
            Assert.Same(
                store,
                scope.ServiceProvider.GetRequiredKeyedService<ICartStore>(CartStoreKeys.Session));
        }
    }

    // ───────────── Thứ tự middleware ─────────────

    [Fact]
    public async Task UseSession_da_duoc_gan_vao_pipeline()
    {
        // Probe được cắm SAU toàn bộ pipeline thật, nên nó chỉ đọc được Session
        // nếu UseSession() đã chạy trước đó.
        //
        // Test này bắt được trường hợp QUÊN GỌI HẲN - đã mutation test.
        //
        // Nó không bắt được sai vị trí, nhưng hoá ra với minimal hosting thì vị trí
        // KHÔNG phải ràng buộc: middleware thực thi endpoint được framework chèn
        // vào cuối pipeline lúc build, nên UseSession() đặt ở đâu trong khối
        // app.UseX() cũng chạy trước Controller. Đã kiểm chứng bằng cách dời
        // UseSession() xuống sát app.Run(): cả CartControllerTests vẫn xanh.
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter, SessionProbeFilter>()));

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/__probe/session");

        Assert.Equal("ghi-doc-duoc", await response.Content.ReadAsStringAsync());
    }

    private sealed class SessionProbeFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);

            app.Run(async context =>
            {
                context.Session.SetString("probe", "ghi-doc-duoc");
                await context.Session.CommitAsync();

                await context.Response.WriteAsync(context.Session.GetString("probe") ?? "khong-doc-duoc");
            });
        };
    }

    public void Dispose() => _factory.Dispose();
}
