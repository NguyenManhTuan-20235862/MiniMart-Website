using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Vì sao test concurrency BẮT BUỘC dùng hai DI scope riêng.
///
/// <para>
/// Bộ test này không kiểm tra nghiệp vụ nào cả - nó kiểm tra **phương pháp** của
/// <see cref="CheckoutConcurrencyTests"/>. Lý do cần tồn tại: dùng chung một scope
/// vẫn cho ra test XANH, nên không có gì tự tố giác khi ai đó "đơn giản hoá" test
/// concurrency bằng cách bỏ <c>CreateScope()</c> đi.
/// </para>
/// <para>
/// Mọi thứ dưới đây là hệ quả trực tiếp của <c>AddDbContext</c> đăng ký vòng đời
/// <b>Scoped</b>: một HTTP request = một DI scope = MỘT <c>DbContext</c>.
/// </para>
/// </summary>
public class DbContextScopeTests : IAsyncLifetime
{
    private const int TonKhoBanDau = 10;

    private readonly WebApplicationFactory<Program> _factory = new();

    private int _categoryId;
    private int _productId;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"SC_{Guid.NewGuid():N}"[..14] };
        var product = new Product { Name = "SanPham", Price = 100_000m, Stock = TonKhoBanDau, Category = category };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productId = product.Id;
    }

    // ───────────── Scoped nghĩa là gì ─────────────

    [Fact]
    public void Trong_MOT_scope_moi_lan_resolve_deu_ra_CUNG_MOT_DbContext()
    {
        using var scope = _factory.Services.CreateScope();

        var lanMot = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
        var lanHai = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Đây chính là định nghĩa của Scoped, và cũng là lý do SaveChangesAsync thuộc
        // về IUnitOfWork chứ không phải từng Repository: mọi Repository trong cùng
        // request dùng chung đúng object này, nên "lưu" luôn là lưu tất cả.
        Assert.Same(lanMot, lanHai);
    }

    [Fact]
    public void Hai_scope_khac_nhau_cho_hai_DbContext_khac_nhau()
    {
        using var scopeMot = _factory.Services.CreateScope();
        using var scopeHai = _factory.Services.CreateScope();

        var contextMot = scopeMot.ServiceProvider.GetRequiredService<MiniMartDbContext>();
        var contextHai = scopeHai.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        // Mỗi HTTP request là một scope. Tạo scope trong test chính là cách mô phỏng
        // "hai request thật" mà không cần dựng hai tiến trình.
        Assert.NotSame(contextMot, contextHai);
    }

    // ───────────── Cái bẫy: Identity Map ─────────────

    [Fact]
    public async Task Trong_MOT_DbContext_doc_hai_lan_tra_ve_CUNG_MOT_OBJECT()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var lanMot = await context.Products.SingleAsync(p => p.Id == _productId);
        var lanHai = await context.Products.SingleAsync(p => p.Id == _productId);

        // Change Tracker là một IDENTITY MAP: mỗi khoá chính chỉ có MỘT object trong
        // một DbContext. Truy vấn thứ hai vẫn chạm DB, nhưng khi vật chất hoá kết quả
        // EF Core thấy khoá đó đã được theo dõi nên TRẢ LẠI object cũ.
        Assert.Same(lanMot, lanHai);
    }

    [Fact]
    public async Task Dung_CHUNG_DbContext_thi_sua_cua_ben_nay_hien_ngay_o_ben_kia()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var nguoiMuaA = await context.Products.SingleAsync(p => p.Id == _productId);
        var nguoiMuaB = await context.Products.SingleAsync(p => p.Id == _productId);

        // "Người mua A" trừ tồn kho - CHƯA lưu, mới chỉ sửa trong bộ nhớ.
        nguoiMuaA.Stock -= 1;

        // ★ ĐÂY là lý do test concurrency dùng chung scope là VÔ NGHĨA.
        //
        // "Người mua B" nhìn thấy ngay con số đã trừ, vì hai biến trỏ cùng một object.
        // Hệ quả trong một test đặt hàng: B đọc Stock đã giảm nên hoặc dừng ở lệnh
        // kiểm của Service, hoặc trừ tiếp trên số đã trừ - KHÔNG có UPDATE thứ hai
        // mang RowVersion cũ, nên KHÔNG có xung đột nào để phát hiện.
        //
        // Test vẫn XANH. Nó chỉ không chứng minh gì về Optimistic Concurrency.
        Assert.Equal(TonKhoBanDau - 1, nguoiMuaB.Stock);
    }

    [Fact]
    public async Task Hai_DbContext_rieng_thi_moi_ben_giu_ban_sao_RIENG()
    {
        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();

        var contextA = scopeA.ServiceProvider.GetRequiredService<MiniMartDbContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var sanPhamA = await contextA.Products.SingleAsync(p => p.Id == _productId);
        var sanPhamB = await contextB.Products.SingleAsync(p => p.Id == _productId);

        Assert.NotSame(sanPhamA, sanPhamB);

        sanPhamA.Stock -= 1;

        // B KHÔNG thấy gì - đúng như hai request thật trên hai máy chủ khác nhau.
        // Mỗi bên giữ RowVersion mà nó đọc được, và đó là điều kiện cần để lần
        // SaveChanges thứ hai có một WHERE lỗi thời mà thất bại.
        Assert.Equal(TonKhoBanDau, sanPhamB.Stock);
    }

    // ───────────── Hệ quả cho Unit of Work ─────────────

    [Fact]
    public async Task Repository_va_UnitOfWork_trong_mot_scope_dung_chung_DbContext()
    {
        using var scope = _factory.Services.CreateScope();

        var productRepository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var product = await productRepository.GetForUpdateAsync(_productId);
        product!.Stock = 7;

        // Repository không hề gọi SaveChanges, vậy mà thay đổi vẫn được lưu - bằng
        // chứng hành vi rằng cả hai đang cầm CÙNG một DbContext. Nếu chúng là hai
        // context khác nhau thì lệnh dưới đây lưu 0 dòng.
        await unitOfWork.SaveChangesAsync();

        using var scopeKiemTra = _factory.Services.CreateScope();
        var context = scopeKiemTra.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        Assert.Equal(7, await context.Products
            .AsNoTracking()
            .Where(p => p.Id == _productId)
            .Select(p => p.Stock)
            .SingleAsync());
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
