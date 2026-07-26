using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure.Data;
using MiniMart.Infrastructure.Repositories;
using MiniMart.Web.Services;

namespace MiniMart.Tests;

/// <summary>
/// Bộ test HỢP ĐỒNG: cùng một loạt khẳng định chạy cho CẢ HAI cài đặt của
/// <see cref="ICartStore"/>.
///
/// <para>
/// Viết một lần rồi kế thừa, thay vì chép đôi, chính là cách kiểm chứng nguyên
/// lý thay thế Liskov: nếu hai kho không thực sự thay thế được nhau thì
/// <c>CartService</c> ở bước 4 sẽ chạy đúng cho người đã đăng nhập và sai cho
/// khách vãng lai - loại bug chỉ lộ ra khi có người thật dùng thử.
/// </para>
/// </summary>
public abstract class CartStoreContractTests : IAsyncLifetime
{
    protected ICartStore Store { get; private set; } = null!;

    protected int ProductA { get; set; } = 1;
    protected int ProductB { get; set; } = 2;

    /// <summary>Dựng kho cần kiểm tra; lớp con lo phần chuẩn bị riêng của nó.</summary>
    protected abstract Task<ICartStore> TaoStoreAsync();

    /// <summary>
    /// Chốt thay đổi xuống nơi lưu trữ.
    ///
    /// Tồn tại vì hai kho KHÔNG đối xứng: DatabaseCartStore chỉ đánh dấu thay đổi
    /// và chờ SaveChanges, còn SessionCartStore ghi ngay. Bất đối xứng này đã được
    /// ghi rõ trên ICartStore; đưa nó vào test để nó hiển hiện chứ không bị giấu.
    /// </summary>
    protected abstract Task ChotAsync();

    public virtual async Task InitializeAsync() => Store = await TaoStoreAsync();

    public virtual Task DisposeAsync() => Task.CompletedTask;

    // ───────────── Đọc ─────────────

    [Fact]
    public async Task Gio_moi_thi_rong_chu_khong_null()
    {
        var lines = await Store.GetLinesAsync();

        Assert.NotNull(lines);
        Assert.Empty(lines);
    }

    // ───────────── Ghi ─────────────

    [Fact]
    public async Task SetQuantity_them_dong_moi()
    {
        await Store.SetQuantityAsync(ProductA, 3);
        await ChotAsync();

        var line = Assert.Single(await Store.GetLinesAsync());

        Assert.Equal(ProductA, line.ProductId);
        Assert.Equal(3, line.Quantity);
    }

    [Fact]
    public async Task SetQuantity_lan_hai_GHI_DE_chu_khong_cong_don()
    {
        await Store.SetQuantityAsync(ProductA, 3);
        await ChotAsync();

        await Store.SetQuantityAsync(ProductA, 5);
        await ChotAsync();

        var line = Assert.Single(await Store.GetLinesAsync());

        // Cộng dồn là NGHIỆP VỤ của "thêm vào giỏ", thuộc CartService. Kho lưu
        // trữ mà tự cộng dồn thì Service không còn cách nào đặt số lượng tuyệt
        // đối cho endpoint UpdateQuantity.
        Assert.Equal(5, line.Quantity);
    }

    [Fact]
    public async Task Hai_san_pham_khac_nhau_thi_thanh_hai_dong()
    {
        await Store.SetQuantityAsync(ProductA, 1);
        await Store.SetQuantityAsync(ProductB, 2);
        await ChotAsync();

        var lines = await Store.GetLinesAsync();

        Assert.Equal(2, lines.Count);
        Assert.Equal(1, lines.Single(l => l.ProductId == ProductA).Quantity);
        Assert.Equal(2, lines.Single(l => l.ProductId == ProductB).Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SetQuantity_khong_duong_thi_nem(int soLuong)
    {
        // Số 0 KHÔNG được ngầm hiểu là xoá: một ý định phải có đúng một đường
        // thực hiện. Và DB có CHECK Quantity > 0 nên số 0 cũng không cất được.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Store.SetQuantityAsync(ProductA, soLuong));
    }

    // ───────────── Xoá ─────────────

    [Fact]
    public async Task Remove_xoa_dung_dong_va_giu_dong_con_lai()
    {
        await Store.SetQuantityAsync(ProductA, 1);
        await Store.SetQuantityAsync(ProductB, 2);
        await ChotAsync();

        await Store.RemoveAsync(ProductA);
        await ChotAsync();

        var line = Assert.Single(await Store.GetLinesAsync());
        Assert.Equal(ProductB, line.ProductId);
    }

    [Fact]
    public async Task Remove_san_pham_khong_co_trong_gio_thi_khong_nem()
    {
        await Store.SetQuantityAsync(ProductA, 1);
        await ChotAsync();

        // Xoá thứ không có sẵn là THÀNH CÔNG - kết quả mong muốn đã đạt. Ném ở
        // đây thì bấm nút xoá hai lần (hoặc mở hai tab) bị báo lỗi vô cớ.
        await Store.RemoveAsync(ProductB);
        await ChotAsync();

        Assert.Single(await Store.GetLinesAsync());
    }

    [Fact]
    public async Task Remove_tren_gio_rong_thi_khong_nem()
    {
        await Store.RemoveAsync(ProductA);
        await ChotAsync();

        Assert.Empty(await Store.GetLinesAsync());
    }

    [Fact]
    public async Task Clear_xoa_sach_gio()
    {
        await Store.SetQuantityAsync(ProductA, 1);
        await Store.SetQuantityAsync(ProductB, 2);
        await ChotAsync();

        await Store.ClearAsync();
        await ChotAsync();

        Assert.Empty(await Store.GetLinesAsync());
    }

    [Fact]
    public async Task Clear_tren_gio_rong_thi_khong_nem()
    {
        await Store.ClearAsync();
        await ChotAsync();

        Assert.Empty(await Store.GetLinesAsync());
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Hợp đồng chạy trên SQL Server thật.</summary>
public class DatabaseCartStoreTests : CartStoreContractTests
{
    private readonly WebApplicationFactory<Program> _factory = new();

    private IServiceScope _scope = null!;
    private MiniMartDbContext _context = null!;
    private int _userId;
    private int _categoryId;

    public override async Task InitializeAsync()
    {
        _scope = _factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var user = new User
        {
            Username = $"cs_{Guid.NewGuid():N}"[..16],
            PasswordHash = "x",
            Role = UserRole.Customer
        };

        var category = new Category { Name = $"CS_{Guid.NewGuid():N}"[..14] };
        var a = new Product { Name = "A", Price = 1_000m, Stock = 100, Category = category };
        var b = new Product { Name = "B", Price = 2_000m, Stock = 100, Category = category };

        _context.Users.Add(user);
        _context.Products.AddRange(a, b);
        await _context.SaveChangesAsync();

        _userId = user.Id;
        _categoryId = category.Id;

        // ProductId phải là id CÓ THẬT: CartItems có khoá ngoại tới Products.
        // Đây là điểm khác biệt duy nhất giữa hai kho ở phần chuẩn bị dữ liệu.
        ProductA = a.Id;
        ProductB = b.Id;

        await base.InitializeAsync();
    }

    protected override Task<ICartStore> TaoStoreAsync() =>
        Task.FromResult<ICartStore>(new DatabaseCartStore(_context, new CurrentUserGia(_userId)));

    protected override Task ChotAsync() => _context.SaveChangesAsync();

    [Fact]
    public async Task Hai_thao_tac_truoc_mot_SaveChanges_khong_lam_mat_dong_nao()
    {
        await Store.SetQuantityAsync(ProductA, 1);
        await Store.SetQuantityAsync(ProductB, 2);
        await ChotAsync();

        // Test này tồn tại vì đã có bug thật ở đây: LayGioAsync truy vấn thẳng DB
        // mỗi lần, nên lần gọi thứ hai chạy Include(c => c.Items) và nạp lại
        // collection từ kết quả truy vấn - mà DB chưa có dòng nào vì chưa
        // SaveChanges - khiến dòng của lần thứ nhất biến mất không dấu vết.
        //
        // Kho Session không dính vì nó ghi ngay; chỉ kho DB mới có "thay đổi
        // đang chờ". Đúng loại bug mà bộ test hợp đồng chung sinh ra để tìm.
        Assert.Equal(1, await _context.Carts.CountAsync(c => c.UserId == _userId));
        Assert.Equal(2, await _context.CartItems.CountAsync(i => i.Cart.UserId == _userId));
    }

    [Fact]
    public async Task Khach_vang_lai_dung_kho_DB_thi_nem_ngay()
    {
        var store = new DatabaseCartStore(_context, new CurrentUserGia(null));

        // Lỗi LẬP TRÌNH (factory ở Program.cs chọn nhầm kho), không phải lỗi
        // người dùng. Phải nổ to thay vì âm thầm thao tác trên giỏ của userId = 0.
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetLinesAsync());
    }

    [Fact]
    public async Task Store_KHONG_tu_goi_SaveChanges()
    {
        await Store.SetQuantityAsync(ProductA, 1);

        // Chưa chốt -> chưa có gì dưới DB. Quy ước dự án: SaveChangesAsync thuộc
        // IUnitOfWork do Service điều khiển, kho lưu trữ chỉ đánh dấu thay đổi.
        using var scopeKhac = _factory.Services.CreateScope();
        var contextKhac = scopeKhac.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        Assert.Equal(0, await contextKhac.CartItems.CountAsync(i => i.Cart.UserId == _userId));
    }

    public override async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.CartItems.Where(i => i.Cart.UserId == _userId).ExecuteDeleteAsync();
        await context.Carts.Where(c => c.UserId == _userId).ExecuteDeleteAsync();
        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => u.Id == _userId).ExecuteDeleteAsync();

        _scope.Dispose();
        _factory.Dispose();
    }

    private sealed class CurrentUserGia(int? id) : ICurrentUser
    {
        public int? Id { get; } = id;
        public bool IsAuthenticated => Id is not null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Hợp đồng chạy trên Session, không chạm DB.</summary>
public class SessionCartStoreTests : CartStoreContractTests
{
    private readonly SessionGia _session = new();

    protected override Task<ICartStore> TaoStoreAsync()
    {
        var httpContext = new DefaultHttpContext { Session = _session };

        // KHÔNG dùng HttpContextAccessor thật: nó giữ HttpContext trong một
        // AsyncLocal, mà AsyncLocal chỉ chảy XUÔI theo chuỗi await. Gán trong
        // InitializeAsync rồi đọc trong thân test là hai nhánh async anh em, nên
        // HttpContext về null và mọi test đổ với thông báo "chưa gọi AddSession".
        return Task.FromResult<ICartStore>(
            new SessionCartStore(new HttpContextAccessorGia(httpContext)));
    }

    private sealed class HttpContextAccessorGia(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    // Session ghi ngay, không có bước chốt nào.
    protected override Task ChotAsync() => Task.CompletedTask;

    [Fact]
    public async Task Gio_rong_thi_BO_HAN_key_khoi_session()
    {
        await Store.SetQuantityAsync(ProductA, 1);
        await Store.RemoveAsync(ProductA);

        // Cất chuỗi "[]" cũng cho kết quả đọc đúng, nhưng Session sẽ phình ra vì
        // giỏ rỗng của mọi bot ghé qua rồi đi.
        Assert.Empty(_session.Keys);
    }

    [Fact]
    public async Task JSON_hong_thi_coi_nhu_gio_rong_chu_khong_nem()
    {
        _session.SetString("MiniMart.Cart", "{khong-phai-json-hop-le");

        // Phiên bản cũ của ứng dụng có thể đã ghi định dạng khác vào cùng key.
        // Ném exception ở đây là mọi trang đều 500 cho tới khi người dùng tự xoá
        // cookie - thứ họ không biết cách làm.
        Assert.Empty(await Store.GetLinesAsync());
    }

    [Fact]
    public async Task Luon_goi_LoadAsync_truoc_khi_doc_session()
    {
        await Store.GetLinesAsync();

        // Đọc session chưa nạp sẽ kích hoạt nạp ĐỒNG BỘ ngầm bên trong, tức
        // sync-over-async - đúng thứ gây nghẽn thread pool dưới tải.
        Assert.True(_session.SoLanLoad > 0);
    }

    /// <summary>ISession trong bộ nhớ, đủ dùng cho test.</summary>
    private sealed class SessionGia : ISession
    {
        private readonly Dictionary<string, byte[]> _kho = [];

        public int SoLanLoad { get; private set; }

        public bool IsAvailable => true;
        public string Id => "session-gia";
        public IEnumerable<string> Keys => _kho.Keys;

        public void Clear() => _kho.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadAsync(CancellationToken cancellationToken = default)
        {
            SoLanLoad++;
            return Task.CompletedTask;
        }

        public void Remove(string key) => _kho.Remove(key);
        public void Set(string key, byte[] value) => _kho[key] = value;

        public bool TryGetValue(string key, out byte[] value) => _kho.TryGetValue(key, out value!);
    }
}
