using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Gợi ý cho ô tìm kiếm ở header: thứ tự xếp hạng, bỏ dấu, và HTML trả về.
///
/// <para>
/// Chạy trên SQL Server THẬT chứ không mock: cả hai tính chất quan trọng nhất của
/// tính năng này — so sánh không phân biệt dấu (<c>Vietnamese_CI_AI</c>) và thứ tự
/// <c>CASE WHEN</c> trong <c>ORDER BY</c> — là hành vi của database engine. Mock hay
/// InMemory sẽ cho kết quả khác và test sẽ xanh vô nghĩa.
/// </para>
/// </summary>
public class ProductSuggestTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private int _categoryId;

    /// <summary>
    /// Dữ liệu phải PHÂN BIỆT ĐƯỢC cả ba mức xếp hạng lẫn quy tắc "tên ngắn hơn lên
    /// trước". Dữ liệu mà mọi dòng cùng hạng thì test không chứng minh được gì.
    ///
    /// <para>
    /// ⚠ Từ khoá là <c>"zumo"</c> — một chuỗi vô nghĩa cố ý. Bản đầu của bộ test này
    /// dùng <c>"pro"</c> và ĐỎ: database dev có 50 sản phẩm thật, nhiều cái chứa "pro"
    /// ("iPhone 16 Pro Max", "MacBook Pro 14"...), nên chúng chiếm hết 8 chỗ của
    /// <c>take</c> và dòng hạng thấp nhất của test bị đẩy ra ngoài. Test khẳng định
    /// THỨ TỰ mà lại dùng từ khoá đụng dữ liệu có sẵn là tự chuốc lấy đỏ ngẫu nhiên.
    /// </para>
    /// </summary>
    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"GY_{Guid.NewGuid():N}"[..14] };

        // Cho từ khoá "zumo" — thứ tự mong đợi chính là thứ tự liệt kê dưới đây:
        category.Products.Add(new Product { Name = "Zumo Aa", Price = 1000m, Stock = 5 });         // hạng 0, dài 7
        category.Products.Add(new Product { Name = "Zumo Bcd", Price = 1000m, Stock = 5 });        // hạng 0, dài 8
        category.Products.Add(new Product { Name = "Zumo Bbbbbb", Price = 1000m, Stock = 5 });     // hạng 0, dài 11
        category.Products.Add(new Product { Name = "MacBook Zumo 14", Price = 1000m, Stock = 5 }); // hạng 1
        category.Products.Add(new Product { Name = "Xyzzumo Nnn", Price = 1000m, Stock = 0 });     // hạng 2

        // Riêng cho phép thử bỏ dấu, không chứa "zumo".
        category.Products.Add(new Product { Name = "Chuột Logitech Zz", Price = 1000m, Stock = 5 });

        // 25 dòng cùng khớp một từ khoá KHÁC, để chứng minh `take` thật sự bị kẹp ở 20.
        for (var i = 1; i <= 25; i++)
        {
            category.Products.Add(new Product { Name = $"Qplot{i:D2}", Price = 1000m, Stock = 5 });
        }

        context.Categories.Add(category);
        await context.SaveChangesAsync();
        _categoryId = category.Id;
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();

        _factory.Dispose();
    }

    private async Task<List<string>> GoiYAsync(string tuKhoa, int take = 8)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();

        var ketQua = await repository.SuggestAsync(tuKhoa, take);

        // Lọc theo danh mục của test này để dữ liệu sẵn có trong DB không làm nhiễu.
        return ketQua.Where(g => g.CategoryName.StartsWith("GY_", StringComparison.Ordinal))
            .Select(g => g.Name)
            .ToList();
    }

    // ───────────── Xếp hạng ─────────────

    [Fact]
    public async Task Khop_DAU_TEN_xep_truoc_khop_dau_mot_tu_va_khop_giua_tu()
    {
        var ten = await GoiYAsync("zumo");

        // Ba dòng đầu bắt đầu bằng "zumo"      -> hạng 0 (trong đó xếp theo độ dài).
        // "MacBook Zumo 14" có một TỪ bắt đầu bằng "zumo" -> hạng 1.
        // "Xyzzumo Nnn" chỉ khớp ở giữa một từ            -> hạng 2.
        Assert.Equal(
            ["Zumo Aa", "Zumo Bcd", "Zumo Bbbbbb", "MacBook Zumo 14", "Xyzzumo Nnn"],
            ten);
    }

    [Fact]
    public async Task Dong_hang_thi_TEN_NGAN_HON_len_truoc()
    {
        var ten = await GoiYAsync("zumo");

        // ★ Cặp này được chọn để PHÂN BIỆT ĐƯỢC hai quy tắc sắp xếp:
        //   theo độ dài  -> "Zumo Bcd" (8)  trước "Zumo Bbbbbb" (11)
        //   theo bảng chữ cái -> "Zumo Bbbbbb" trước "Zumo Bcd"  (vì 'b' < 'c')
        // Hai thứ tự NGƯỢC nhau, nên test này đỏ ngay nếu ai đó bỏ ThenBy(Name.Length).
        var iNgan = ten.IndexOf("Zumo Bcd");
        var iDai = ten.IndexOf("Zumo Bbbbbb");

        Assert.True(iNgan >= 0 && iDai >= 0);
        Assert.True(iNgan < iDai, $"Thứ tự sai: {string.Join(" | ", ten)}");
    }

    [Fact]
    public async Task Cang_go_NHIEU_thi_ket_qua_cang_HEP()
    {
        var mot = await GoiYAsync("zum");
        var hai = await GoiYAsync("zumo b");
        var ba = await GoiYAsync("zumo bc");

        // Đây chính là "càng nhập nhiều càng thu hẹp" — nó là hệ quả tự nhiên của
        // LIKE, nhưng vẫn phải có test vì nó là lời hứa với người dùng.
        Assert.True(mot.Count > hai.Count, $"{mot.Count} -> {hai.Count}");
        Assert.True(hai.Count > ba.Count, $"{hai.Count} -> {ba.Count}");
        Assert.Equal(["Zumo Bcd"], ba);
    }

    // ───────────── Dấu và hoa/thường ─────────────

    [Fact]
    public async Task Go_KHONG_DAU_van_tim_duoc_ten_co_dau()
    {
        // ★ Lý do tính năng này cần collation riêng. Collation mặc định của database
        // là SQL_Latin1_General_CP1_CI_AS — chữ AS cuối nghĩa là CÓ phân biệt dấu,
        // nên `LIKE N'%chuot%'` trả về 0 dòng trong khi shop đang bán "Chuột...".
        // Đã đo trực tiếp bằng sqlcmd trước khi viết code.
        var ten = await GoiYAsync("chuot");

        Assert.Contains("Chuột Logitech Zz", ten);
    }

    [Fact]
    public async Task Go_HOA_hay_THUONG_deu_ra_cung_ket_qua()
    {
        Assert.Equal(await GoiYAsync("zumo"), await GoiYAsync("ZUMO"));
    }

    // ───────────── Biên ─────────────

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public async Task Tu_khoa_qua_ngan_tra_ve_RONG_chu_khong_tra_ca_kho(string tuKhoa)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();

        // Một ký tự khớp gần như mọi thứ; dropdown 8 dòng ngẫu nhiên tệ hơn là không
        // có dropdown nào. Đây cũng là hàng rào rẻ nhất chống việc mỗi phím gõ là một
        // lần quét cả bảng.
        Assert.Empty(await repository.SuggestAsync(tuKhoa));
    }

    [Fact]
    public async Task Khong_khop_gi_thi_rong_chu_khong_loi()
    {
        Assert.Empty(await GoiYAsync("khongtontai_xyz_987"));
    }

    [Fact]
    public async Task Take_bi_kep_de_khong_ai_keo_ca_kho_ve()
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();

        var ketQua = await repository.SuggestAsync("a", take: 999_999);
        Assert.Empty(ketQua);   // "a" quá ngắn nên rỗng bất kể take

        // 25 sản phẩm "Qplot##" cùng khớp, nên trần 20 là thứ DUY NHẤT giải thích được
        // con số trả về. Nếu dữ liệu ít hơn trần thì `<= 20` xanh mà không chứng minh gì.
        var nhieu = await repository.SuggestAsync("qplot", take: 999_999);
        Assert.Equal(20, nhieu.Count);
    }

    // ───────────── HTTP + HTML ─────────────

    [Fact]
    public async Task Endpoint_tra_ve_HTML_khong_kem_layout()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/Product/Suggest?tuKhoa=zumo");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // PartialView chứ không View: trình duyệt đã có sẵn trang rồi.
        Assert.DoesNotContain("<!DOCTYPE", html);
        Assert.DoesNotContain("navbar", html);
        Assert.Contains("data-goi-y", html);
    }

    [Fact]
    public async Task KHONG_duoc_lo_con_so_ton_kho()
    {
        const int tonKhoDacBiet = 8_675_309;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            context.Products.Add(new Product
            {
                Name = "Prozzz Ton Kho",
                Price = 1000m,
                Stock = tonKhoDacBiet,
                CategoryId = _categoryId
            });
            await context.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/Product/Suggest?tuKhoa=prozzz");

        // Bảo đảm này đến từ CẤU TRÚC: ProductSuggestion chỉ có bool ConHang, không có
        // Stock để mà lỡ in ra. Test vẫn cần vì cấu trúc có thể bị nới ra sau này.
        Assert.Contains("Prozzz Ton Kho", html);
        Assert.DoesNotContain(tonKhoDacBiet.ToString(), html);
        Assert.DoesNotContain("RowVersion", html);
    }

    [Fact]
    public async Task Ten_san_pham_duoc_Razor_escape()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            context.Products.Add(new Product
            {
                Name = "Proxss <script>alert(1)</script>",
                Price = 1000m,
                Stock = 5,
                CategoryId = _categoryId
            });
            await context.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/Product/Suggest?tuKhoa=proxss");

        // ★ Đây là lý do endpoint này trả PartialView chứ không Json. Chuỗi được chèn
        // thẳng vào DOM là TÊN SẢN PHẨM; với JSON thì JSON không escape ký tự '<' và
        // trách nhiệm escape rơi vào JavaScript, quên là XSS.
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }
}
