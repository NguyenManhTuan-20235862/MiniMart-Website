using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Trang chi tiết sản phẩm (<c>/Product/Details/{id}</c>) qua pipeline HTTP thật.
///
/// <para>
/// Trước phase này <c>/Product/5</c> trả 404 vì controller khách hàng chỉ có
/// <c>LoadMore</c>. Đây là trang cuối cùng còn thiếu để một khách đi trọn vòng
/// xem → giỏ → đặt → trả tiền → xem lại đơn.
/// </para>
/// </summary>
public class ProductDetailTests : IAsyncLifetime
{
    /// <summary>
    /// 7 chữ số nên không thể trùng ngẫu nhiên với một Id hay một mẩu giá tiền
    /// trong HTML — cùng lý do với <c>ProductLoadMoreTests</c>. Assert trên chuỗi
    /// số ngắn kiểu "77" sẽ đỏ ngẫu nhiên vì GUID hex có chứa chữ số.
    /// </summary>
    private const int TonKhoDacBiet = 8_675_309;

    private readonly WebApplicationFactory<Program> _factory = new();

    private int _categoryId;
    private int _categoryLeLoiId;
    private string _tenDanhMuc = "";

    private int _idConHang;
    private int _idHetHang;
    private int _idLeLoi;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        _tenDanhMuc = $"CT_{Guid.NewGuid():N}"[..14];
        var category = new Category { Name = _tenDanhMuc };

        // Sản phẩm đang xem. Tên bắt đầu bằng "ZZ" để nó đứng CUỐI khi sắp theo tên —
        // nhờ vậy test "không gợi ý chính nó" không thể xanh nhờ may mắn về thứ tự.
        var dangXem = new Product
        {
            Name = "ZZ_DangXem",
            Price = 1_234_000m,
            Stock = TonKhoDacBiet,
            ImageUrl = "/images/products/dangxem.jpg",
            Category = category
        };

        var hetHang = new Product
        {
            Name = "ZY_HetHang",
            Price = 999_000m,
            Stock = 0,
            Category = category
        };

        // ★ Món hết hàng này đứng ĐẦU BẢNG CHỮ CÁI trong danh mục.
        //
        // Nếu truy vấn gợi ý chỉ OrderBy(Name) thì nó chiếm mất một trong bốn chỗ và
        // "EE" bị đẩy ra. Đó chính là điều mà OrderByDescending(p => p.Stock > 0) ngăn,
        // và là lý do dữ liệu test được dựng đúng như thế này.
        context.Products.Add(new Product
        {
            Name = "AA_HetHang",
            Price = 111_000m,
            Stock = 0,
            Category = category
        });

        foreach (var ten in new[] { "BB", "CC", "DD", "EE" })
        {
            context.Products.Add(new Product
            {
                Name = ten,
                Price = 222_000m,
                Stock = 7,
                Category = category
            });
        }

        // Danh mục thứ hai chỉ có ĐÚNG một sản phẩm: đường "không có gợi ý nào".
        var danhMucLeLoi = new Category { Name = $"CL_{Guid.NewGuid():N}"[..14] };
        var leLoi = new Product
        {
            Name = "MotMinh",
            Price = 55_000m,
            Stock = 3,
            Category = danhMucLeLoi
        };

        context.Products.AddRange(dangXem, hetHang, leLoi);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _categoryLeLoiId = danhMucLeLoi.Id;
        _idConHang = dangXem.Id;
        _idHetHang = hetHang.Id;
        _idLeLoi = leLoi.Id;
    }

    private HttpClient CreateClient(bool theoRedirect = true) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = theoRedirect });

    private async Task<string> LayHtmlAsync(int id)
    {
        using var client = CreateClient();

        return await client.GetStringAsync($"/Product/Details/{id}");
    }

    /// <summary>
    /// Bóc riêng khối gợi ý. Assert trên cả trang là không phân biệt được "BB xuất
    /// hiện trong khối gợi ý" với "BB xuất hiện ở đâu đó khác" — mà một test không
    /// phân biệt được hai điều đó thì không chứng minh được điều nào.
    /// </summary>
    private static string BocKhoiGoiY(string html)
    {
        var viTri = html.IndexOf("data-san-pham-lien-quan", StringComparison.Ordinal);

        Assert.True(viTri >= 0, "Không tìm thấy khối gợi ý trong HTML.");

        return html[viTri..];
    }

    /// <summary>
    /// Bóc riêng khối thông tin của sản phẩm ĐANG XEM, cắt bỏ phần gợi ý phía dưới.
    ///
    /// <para>
    /// Cần thiết vì khu gợi ý cũng render <c>_ProductCard</c>, tức cũng có form
    /// <c>/Cart/Add</c> của riêng nó. Kiểm "sản phẩm hết hàng thì không có nút thêm
    /// vào giỏ" trên toàn trang sẽ đỏ dù code hoàn toàn đúng — và tệ hơn, kiểm chiều
    /// ngược lại trên toàn trang sẽ XANH kể cả khi nút của sản phẩm chính biến mất,
    /// vì nút của một món gợi ý vẫn đứng đó.
    /// </para>
    /// </summary>
    private static string BocKhoiChinh(string html)
    {
        var batDau = html.IndexOf("data-product-detail", StringComparison.Ordinal);

        Assert.True(batDau >= 0, "Không tìm thấy khối chi tiết sản phẩm trong HTML.");

        var ketThuc = html.IndexOf("data-san-pham-lien-quan", batDau, StringComparison.Ordinal);

        // Không có khu gợi ý (danh mục chỉ có một sản phẩm) thì lấy tới hết trang.
        return ketThuc < 0 ? html[batDau..] : html[batDau..ketThuc];
    }

    // ───────────── Trang tồn tại và hiển thị đúng thứ ─────────────

    [Fact]
    public async Task Trang_chi_tiet_mo_duoc_ma_khong_can_dang_nhap()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/Product/Details/{_idConHang}");

        // Xem hàng là việc của mọi người. Có [Authorize] ở đây thì khách vãng lai
        // bị đá sang trang đăng nhập trước cả khi biết shop bán gì.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Hien_ten_gia_va_danh_muc()
    {
        var html = await LayHtmlAsync(_idConHang);

        Assert.Contains("ZZ_DangXem", html);
        // MoneyFormat khoá vào InvariantCulture nên dấu phân cách là "," bất kể
        // locale của máy chạy test. Gọi thẳng ToString("N0") thì máy vi-VN in
        // "1.234.000" và assertion này đỏ khi đổi máy.
        Assert.Contains("1,234,000", html);
        Assert.Contains(_tenDanhMuc, html);
    }

    [Fact]
    public async Task Co_breadcrumb_quay_ve_danh_muc_dang_xem()
    {
        var html = await LayHtmlAsync(_idConHang);

        // Người dùng vào đây từ link chia sẻ thì không có nút Back nào đưa họ về
        // danh sách. Breadcrumb là đường quay lại DUY NHẤT.
        Assert.Contains($"categoryId={_categoryId}", html);
    }

    [Fact]
    public async Task Dung_dung_layout_cua_trang_khach_hang()
    {
        var html = await LayHtmlAsync(_idConHang);

        // Layout được phân giải lúc CHẠY, không phải lúc biên dịch: sai tên trong
        // _ViewStart thì dotnet build vẫn qua và chỉ nổ khi mở trang.
        Assert.Contains("<!DOCTYPE", html);
        Assert.Contains("ElectroShop", html);
        // KHÔNG được rơi vào layout của khu vực quản trị.
        Assert.DoesNotContain("Quản trị", html);
    }

    // ───────────── Id sai ─────────────

    [Fact]
    public async Task San_pham_khong_ton_tai_tra_404()
    {
        using var client = CreateClient(theoRedirect: false);

        var response = await client.GetAsync("/Product/Details/999999999");

        // 404 chứ không phải 200 kèm trang trống: trả 200 là mời công cụ tìm kiếm
        // lập chỉ mục một trang rỗng, và người dùng không phân biệt được "sản phẩm
        // này không có" với "trang bị lỗi".
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Id_khong_phai_so_tra_404()
    {
        using var client = CreateClient(theoRedirect: false);

        // Model binder cho id = 0 khi không parse được -> không sản phẩm nào khớp.
        var response = await client.GetAsync("/Product/Details/khong-phai-so");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ───────────── Ràng buộc lộ dữ liệu ─────────────

    [Fact]
    public async Task TUYET_DOI_khong_lo_con_so_ton_kho()
    {
        var html = await LayHtmlAsync(_idConHang);

        // ★ Trang này là chỗ dễ vi phạm quy ước "chỉ hiện trạng thái còn/hết hàng"
        // nhất trong cả dự án: phản xạ tự nhiên khi làm ô nhập số lượng là viết
        // max="@Model.Product.Stock", và thế là bảng tồn kho của toàn shop nằm sẵn
        // trong HTML cho bất kỳ ai chạy một vòng curl.
        //
        // Trần 100 dưới form là trần của AddToCartRequest, không phải tồn kho.
        Assert.DoesNotContain(TonKhoDacBiet.ToString(), html);
        Assert.Contains("Còn hàng", BocKhoiChinh(html));
        Assert.Contains("max=\"100\"", BocKhoiChinh(html));
    }

    [Fact]
    public async Task Khong_lo_RowVersion_ra_HTML()
    {
        var html = await LayHtmlAsync(_idConHang);

        // Trang chi tiết là đường ĐỌC, không có form sửa nào nên không có lý do gì
        // để concurrency token đi ra ngoài.
        Assert.DoesNotContain("RowVersion", html);
    }

    [Fact]
    public async Task Ten_san_pham_duoc_Razor_escape()
    {
        int id;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            var doc = new Product
            {
                Name = "<script>alert(1)</script>",
                Price = 1m,
                Stock = 1,
                CategoryId = _categoryId
            };

            context.Products.Add(doc);
            await context.SaveChangesAsync();
            id = doc.Id;
        }

        var html = await LayHtmlAsync(id);

        // Tên sản phẩm đi vào CẢ nội dung thẻ lẫn thuộc tính alt/aria-label, nên
        // đây là ba đường XSS trong một trang. Razor escape cả ba, JavaScript thì không.
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    // ───────────── Nút thêm vào giỏ ─────────────

    [Fact]
    public async Task Con_hang_thi_co_form_them_vao_gio_voi_o_nhap_so_luong()
    {
        var chinh = BocKhoiChinh(await LayHtmlAsync(_idConHang));

        Assert.Contains("/Cart/Add", chinh);
        Assert.Contains($"name=\"ProductId\" value=\"{_idConHang}\"", chinh);
        // Ô số lượng phải NHẬP ĐƯỢC, khác thẻ sản phẩm (hidden, cố định 1).
        // Đó là điểm khác biệt chính về công năng giữa hai chỗ.
        Assert.Contains("name=\"Quantity\"", chinh);
        Assert.Contains("type=\"number\"", chinh);
    }

    [Fact]
    public async Task Het_hang_thi_KHONG_co_form_them_vao_gio()
    {
        var html = await LayHtmlAsync(_idHetHang);

        // Không hiện nút bấm vào ra lỗi — cùng nguyên tắc với đơn Pending ở trang
        // "Đơn hàng của tôi": nói rõ là chưa làm được, thay vì mời người dùng bấm
        // một nút chỉ để nhận lời từ chối.
        //
        // Bóc khối chính trước khi assert: khu gợi ý bên dưới cũng có form
        // /Cart/Add của riêng nó, nên quét cả trang là đỏ dù code đúng.
        Assert.Contains("Hết hàng", BocKhoiChinh(html));
        Assert.DoesNotContain("/Cart/Add", BocKhoiChinh(html));
    }

    [Fact]
    public async Task Them_vao_gio_tu_trang_chi_tiet_dung_SO_LUONG_da_chon()
    {
        using var client = CreateClient();

        var response = await client.PostFormAsync("/Cart/Add", new Dictionary<string, string>
        {
            ["ProductId"] = _idConHang.ToString(),
            ["Quantity"] = "3"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Form thường (không AJAX) đi theo Post-Redirect-Get nên client đã ở /Cart.
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("ZZ_DangXem", html);
        // 3 x 1.234.000. Kiểm TỔNG chứ không kiểm số "3": số 3 xuất hiện khắp nơi
        // trong HTML, còn con số này thì không.
        Assert.Contains("3,702,000", html);
    }

    // ───────────── Hook data-* cho JavaScript ─────────────

    [Theory]
    [InlineData("data-product-detail")]
    [InlineData("data-ten-san-pham")]
    [InlineData("data-gia-ban")]
    [InlineData("data-tinh-trang-hang")]
    [InlineData("data-so-luong")]
    [InlineData("data-cart-add")]
    public async Task Cac_hook_data_van_con_trong_HTML(string hook)
    {
        var html = await LayHtmlAsync(_idConHang);

        // Đổi tên một data-* trong Razor làm querySelector trả null và JS im lặng
        // ngừng hoạt động — không lỗi build, không lỗi runtime. data-cart-add là
        // cái đắt nhất: mất nó thì nút "Thêm vào giỏ" rời khỏi đường AJAX và quay
        // về full page reload mà không có gì báo.
        Assert.Contains(hook, html);
    }

    // ───────────── Thẻ sản phẩm dẫn tới đây ─────────────

    [Fact]
    public async Task The_san_pham_o_trang_chu_link_sang_trang_chi_tiet()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync($"/?categoryId={_categoryId}");

        // Không có link này thì trang chi tiết tồn tại mà không ai tới được —
        // đúng loại "làm xong nhưng chưa nối vào" mà chỉ test đường đi mới bắt.
        Assert.Contains($"/Product/Details/{_idConHang}", html);
    }

    [Fact]
    public async Task Link_chi_tiet_KHONG_boc_ca_the_san_pham()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync($"/?categoryId={_categoryId}");

        // <form> lồng trong <a> là HTML không hợp lệ: trình duyệt tự sửa cây DOM
        // theo cách của nó và nút "Thêm vào giỏ" hỏng theo những kiểu khó đoán.
        //
        // ★ Bản đầu của test này dùng regex `<a\b[^>]*>(.*?)</a>` rồi kiểm "<form"
        // trong nhóm bắt được, và nó LỌT mutation "bọc cả thẻ trong <a>": vì
        // non-greedy nên với thẻ <a> ngoài cùng nó khớp tới </a> ĐẦU TIÊN — của
        // link ảnh bên trong — và không bao giờ nhìn thấy cái <form>. Regex không
        // đếm được cấu trúc lồng nhau; phải tự đếm độ sâu.
        var moc = Regex.Matches(html, "<a\\b|</a>|<form\\b", RegexOptions.IgnoreCase);
        var doSau = 0;

        foreach (Match m in moc)
        {
            if (m.Value.Equals("</a>", StringComparison.OrdinalIgnoreCase))
            {
                doSau--;
            }
            else if (m.Value.StartsWith("<a", StringComparison.OrdinalIgnoreCase))
            {
                doSau++;
                Assert.True(doSau <= 1, $"Có <a> lồng trong <a> tại vị trí {m.Index}.");
            }
            else
            {
                Assert.True(doSau == 0, $"Có <form> nằm trong <a> tại vị trí {m.Index}.");
            }
        }
    }

    // ───────────── Gợi ý cùng danh mục ─────────────

    [Fact]
    public async Task Goi_y_chi_lay_san_pham_CUNG_danh_muc()
    {
        var goiY = BocKhoiGoiY(await LayHtmlAsync(_idConHang));

        Assert.Contains("BB", goiY);
        // "MotMinh" thuộc danh mục khác. Quên điều kiện CategoryId thì gợi ý biến
        // thành "vài sản phẩm bất kỳ trong shop".
        Assert.DoesNotContain("MotMinh", goiY);
    }

    [Fact]
    public async Task Goi_y_KHONG_gom_chinh_san_pham_dang_xem()
    {
        var goiY = BocKhoiGoiY(await LayHtmlAsync(_idConHang));

        // Không có exception nào báo lỗi này — nó chỉ trông rất ngớ ngẩn: gợi ý
        // người dùng bấm vào đúng trang họ đang đứng.
        Assert.DoesNotContain($"/Product/Details/{_idConHang}", goiY);
    }

    [Fact]
    public async Task Goi_y_UU_TIEN_san_pham_con_hang()
    {
        var goiY = BocKhoiGoiY(await LayHtmlAsync(_idConHang));

        // ★ "AA_HetHang" đứng ĐẦU bảng chữ cái trong danh mục. Nếu truy vấn chỉ
        // OrderBy(Name) thì nó chiếm một trong bốn chỗ và "EE" bị đẩy ra.
        //
        // Gợi ý một món không mua được là gợi ý vô ích, và đây là loại sai không
        // có triệu chứng nào: trang vẫn hiện đủ bốn thẻ, chỉ là một thẻ vô dụng.
        Assert.DoesNotContain("AA_HetHang", goiY);

        foreach (var ten in new[] { "BB", "CC", "DD", "EE" })
        {
            Assert.Contains($">{ten}</a>", goiY);
        }
    }

    [Fact]
    public async Task Goi_y_gioi_han_dung_bon_san_pham()
    {
        var goiY = BocKhoiGoiY(await LayHtmlAsync(_idConHang));

        // Danh mục có 7 sản phẩm khác. Không giới hạn thì khu "gợi ý" thành một
        // bản sao thứ hai của trang danh mục, ngay dưới trang chi tiết.
        Assert.Equal(4, goiY.Split("class=\"card h-100 shadow-sm\"").Length - 1);
    }

    [Fact]
    public async Task Danh_muc_chi_co_MOT_san_pham_thi_khong_hien_khu_goi_y()
    {
        var html = await LayHtmlAsync(_idLeLoi);

        // Danh sách rỗng là kết cục BÌNH THƯỜNG, không phải lỗi: view chỉ việc
        // không render khu vực đó. Hiện một tiêu đề "Sản phẩm tương tự" bên trên
        // một khoảng trắng là tệ hơn không hiện gì.
        Assert.Contains("MotMinh", html);
        Assert.DoesNotContain("Sản phẩm tương tự", html);
        Assert.DoesNotContain("data-san-pham-lien-quan", html);
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products
            .Where(p => p.CategoryId == _categoryId || p.CategoryId == _categoryLeLoiId)
            .ExecuteDeleteAsync();

        await context.Categories
            .Where(c => c.Id == _categoryId || c.Id == _categoryLeLoiId)
            .ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
