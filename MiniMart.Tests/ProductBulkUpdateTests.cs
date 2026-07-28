using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Application.Interfaces;
using MiniMart.Application.Models;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Sửa giá / tồn kho hàng loạt trên <b>SQL Server thật</b>.
///
/// <para>
/// BẮT BUỘC không dùng InMemory: ba tính chất được kiểm ở đây - <c>RowVersion</c> chặn
/// ghi đè, cả batch cùng bị bỏ khi một dòng lệch, và dòng không đổi thì không sinh câu
/// UPDATE - đều là hành vi của database engine. InMemory sẽ cho tất cả xanh kể cả khi
/// code không hề round-trip phiên bản nào.
/// </para>
/// <para>
/// "Người khác" luôn là một <b>DI scope riêng</b>. Dùng chung scope thì hai bên nhìn
/// cùng một object trong Change Tracker, không UPDATE nào mang phiên bản cũ, và không
/// có xung đột nào để phát hiện.
/// </para>
/// </summary>
public class ProductBulkUpdateTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernames = [];

    private int _categoryId;
    private int _idA;
    private int _idB;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"BU_{Guid.NewGuid():N}"[..14] };
        var a = new Product { Name = "Bulk A", Price = 100_000m, Stock = 10, Category = category };
        var b = new Product { Name = "Bulk B", Price = 200_000m, Stock = 20, Category = category };

        context.Products.AddRange(a, b);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _idA = a.Id;
        _idB = b.Id;
    }

    // ───────────── Tầng Service, trên DB thật ─────────────

    [Fact]
    public async Task Luu_nhieu_dong_thi_ghi_het_va_tra_ve_dung_so_dong()
    {
        var (rvA, rvB) = await LayHaiPhienBanAsync();

        var ketQua = await TrongScopeAsync(svc => svc.BulkUpdatePriceStockAsync(
        [
            new ProductBulkUpdateItem(_idA, 111_000m, 1, rvA),
            new ProductBulkUpdateItem(_idB, 222_000m, 2, rvB)
        ]));

        Assert.Equal(2, ketQua.SoDongDaLuu);
        Assert.False(ketQua.CoXungDot);

        var a = await LaySanPhamAsync(_idA);
        var b = await LaySanPhamAsync(_idB);
        Assert.Equal((111_000m, 1), (a.Price, a.Stock));
        Assert.Equal((222_000m, 2), (b.Price, b.Stock));
    }

    [Fact]
    public async Task Dong_xung_dot_bi_BO_QUA_dong_con_lai_VAN_duoc_ghi()
    {
        var (rvA, rvB) = await LayHaiPhienBanAsync();

        // Người khác sửa GIÁ của A -> RowVersion của A đổi, phiên bản ta đang cầm hết hạn.
        await SuaTrucTiepAsync(_idA, p => p.Price = 999_000m);

        var ketQua = await TrongScopeAsync(svc => svc.BulkUpdatePriceStockAsync(
        [
            new ProductBulkUpdateItem(_idA, 111_000m, 1, rvA),
            new ProductBulkUpdateItem(_idB, 222_000m, 2, rvB)
        ]));

        // ★ Yêu cầu cốt lõi, và là chỗ khác hẳn CheckoutAsync: B hoàn toàn hợp lệ nên
        // B ĐƯỢC ghi, dù A hỏng.
        //
        // Ở luồng đặt hàng thì ngược lại - một món hết hàng phải huỷ cả đơn, vì nửa vời
        // nghĩa là khách trả tiền cho một đơn không đúng thứ họ đặt. Ở đây người dùng là
        // Admin đang nhìn màn hình, và "1 dòng đã lưu, 1 dòng cần xem lại" là câu họ xử
        // lý được ngay. Cùng một cơ chế RowVersion, hai ngữ nghĩa khác nhau - và sự khác
        // nhau đó là quyết định nghiệp vụ, không phải chi tiết kỹ thuật.
        Assert.Equal(1, ketQua.SoDongDaLuu);
        Assert.Equal(222_000m, (await LaySanPhamAsync(_idB)).Price);

        // A giữ nguyên giá trị của NGƯỜI KIA, không bị ghi đè - đúng thứ RowVersion
        // sinh ra để giữ.
        Assert.Equal(999_000m, (await LaySanPhamAsync(_idA)).Price);

        // Và A được báo lại kèm giá trị hiện tại, đủ để Admin quyết định.
        var xungDot = Assert.Single(ketQua.XungDot);
        Assert.Equal(_idA, xungDot.ProductId);
        Assert.Equal("Bulk A", xungDot.ProductName);
        Assert.Equal(999_000m, xungDot.PriceHienTai);
        Assert.False(xungDot.DaBiXoa);
    }

    [Fact]
    public async Task San_pham_bi_xoa_that_thi_bao_lai_chu_khong_chan_dong_con_lai()
    {
        var (rvA, rvB) = await LayHaiPhienBanAsync();

        await XoaTrucTiepAsync(_idA);

        var ketQua = await TrongScopeAsync(svc => svc.BulkUpdatePriceStockAsync(
        [
            new ProductBulkUpdateItem(_idA, 111_000m, 1, rvA),
            new ProductBulkUpdateItem(_idB, 222_000m, 2, rvB)
        ]));

        Assert.Equal(1, ketQua.SoDongDaLuu);
        Assert.Equal(222_000m, (await LaySanPhamAsync(_idB)).Price);
        Assert.True(Assert.Single(ketQua.XungDot).DaBiXoa);
    }

    [Fact]
    public async Task Dong_KHONG_sua_gi_thi_phien_ban_cu_van_luu_duoc()
    {
        var (rvA, rvB) = await LayHaiPhienBanAsync();

        // Người khác đổi TÊN của A. Bảng sửa hàng loạt không có ô tên nên thao tác này
        // không đụng gì tới việc ta đang làm - nhưng nó vẫn làm RowVersion của A đổi.
        await SuaTrucTiepAsync(_idA, p => p.Name = "Ten moi");

        var ketQua = await TrongScopeAsync(svc => svc.BulkUpdatePriceStockAsync(
        [
            // A gửi lên ĐÚNG giá trị cũ - người dùng không sửa dòng này.
            new ProductBulkUpdateItem(_idA, 100_000m, 10, rvA),
            new ProductBulkUpdateItem(_idB, 222_000m, 2, rvB)
        ]));

        // ★ Tính chất riêng của đường change tracking, và là lý do chính chọn nó.
        //
        // Gán giá trị BẰNG giá trị đang có không đánh dấu Modified, nên A không sinh câu
        // UPDATE nào - phiên bản cũ của A không bao giờ được kẹp vào WHERE nào cả.
        // Với ExecuteUpdate hay Dapper thì mọi dòng đều bị UPDATE bất kể có sửa hay
        // không, và dòng A này sẽ làm hỏng cả lần lưu dù người dùng chẳng chạm vào nó.
        Assert.Equal(1, ketQua.SoDongDaLuu);

        // ★★ Và nó KHÔNG được báo là xung đột. Đây là ranh giới tinh tế của yêu cầu
        // "bỏ qua dòng lệch phiên bản và báo lại": chỉ báo dòng người dùng THẬT SỰ định
        // ghi. Bảng này chỉ có Giá và Tồn kho, nên người khác đổi TÊN không phải là thứ
        // Admin cần biết ở đây - báo là dạy họ bỏ qua thông báo.
        Assert.False(ketQua.CoXungDot);

        Assert.Equal(222_000m, (await LaySanPhamAsync(_idB)).Price);
        Assert.Equal("Ten moi", (await LaySanPhamAsync(_idA)).Name);
    }

    [Fact]
    public async Task Sau_xung_dot_lan_luu_thu_hai_thi_thanh_cong()
    {
        var (rvA, _) = await LayHaiPhienBanAsync();
        await SuaTrucTiepAsync(_idA, p => p.Price = 999_000m);

        var lanMot = await TrongScopeAsync(svc => svc.BulkUpdatePriceStockAsync(
            [new ProductBulkUpdateItem(_idA, 111_000m, 1, rvA)]));

        Assert.True(lanMot.CoXungDot);
        Assert.Equal(0, lanMot.SoDongDaLuu);

        // Lưu lại bằng phiên bản MỚI mà chính lần gọi trước đã trả về - đúng việc
        // Controller làm khi render lại bảng. Không có bước này thì người dùng bấm Lưu
        // bao nhiêu lần cũng nhận đúng một lỗi và không có đường nào ra.
        var rvAMoi = lanMot.XungDot[0].RowVersionHienTai;

        var lanHai = await TrongScopeAsync(svc => svc.BulkUpdatePriceStockAsync(
            [new ProductBulkUpdateItem(_idA, 111_000m, 1, rvAMoi)]));

        Assert.Equal(1, lanHai.SoDongDaLuu);
        Assert.False(lanHai.CoXungDot);
        Assert.Equal(111_000m, (await LaySanPhamAsync(_idA)).Price);
    }

    // ───────────── Endpoint POST ─────────────

    [Fact]
    public async Task POST_hop_le_thi_redirect_ve_dung_trang_va_ghi_du_lieu()
    {
        using var client = await TaoClientAdminAsync();
        var (rvA, rvB) = await LayHaiPhienBanAsync();

        var response = await client.PostFormAsync("/Admin/Product/BulkEdit",
            DungForm(page: 3, (_idA, 111_000m, 1, rvA), (_idB, 222_000m, 2, rvB)));

        // Post-Redirect-Get cho nhánh THÀNH CÔNG: F5 sau khi lưu không gửi lại POST.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        // Quay lại đúng trang vừa sửa, không phải trang 1.
        Assert.Contains("page=3", response.Headers.Location!.ToString(), StringComparison.Ordinal);

        Assert.Equal(111_000m, (await LaySanPhamAsync(_idA)).Price);
        Assert.Equal(222_000m, (await LaySanPhamAsync(_idB)).Price);
    }

    [Fact]
    public async Task POST_gia_khong_hop_le_thi_render_lai_bang_va_KHONG_ghi_gi()
    {
        using var client = await TaoClientAdminAsync();
        var (rvA, rvB) = await LayHaiPhienBanAsync();

        var response = await client.PostFormAsync("/Admin/Product/BulkEdit",
            DungForm(page: 1, (_idA, 0m, 1, rvA), (_idB, 222_000m, 2, rvB)));

        var html = await response.Content.ReadAsStringAsync();

        // Render LẠI, không redirect: redirect vứt sạch giá và tồn kho vừa gõ cho cả
        // 20 dòng. Khẳng định "vẫn đang ở đúng trang" chứ không chỉ khẳng định nội
        // dung - trang khác cũng in ra tên sản phẩm và cũng sẽ làm assertion xanh.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Items[0].Price", html, StringComparison.Ordinal);

        // Name có [BindNever] nên sau model binding nó rỗng. Không nạp lại thì bảng
        // hiện ra với cột tên trắng trơn, và chuỗi rỗng thì không có lỗi nào báo.
        Assert.Contains("Bulk A", html, StringComparison.Ordinal);

        // Một dòng hỏng validation là KHÔNG dòng nào được ghi, kể cả dòng hợp lệ.
        Assert.Equal(200_000m, (await LaySanPhamAsync(_idB)).Price);
    }

    [Fact]
    public async Task POST_phien_ban_cu_thi_LUU_dong_con_lai_va_bao_lai_dong_bi_bo_qua()
    {
        using var client = await TaoClientAdminAsync();
        var (rvA, rvB) = await LayHaiPhienBanAsync();

        await SuaTrucTiepAsync(_idA, p => p.Price = 999_000m);

        var response = await client.PostFormAsync("/Admin/Product/BulkEdit",
            DungForm(page: 1, (_idA, 111_000m, 1, rvA), (_idB, 222_000m, 2, rvB)));

        // ★ HtmlDecode TRƯỚC mọi assertion so chuỗi Base64.
        //
        // Base64 của rowversion đôi khi chứa '+', mà HtmlEncoder mã hoá thành "&#x2B;".
        // So chuỗi Base64 thô với HTML thô thì Assert.Contains trượt NGẪU NHIÊN - chỉ
        // khi giá trị tình cờ có dấu '+'. Cùng bug đã làm ProductConcurrencyTests và
        // ProductBulkEditPageTests đỏ ngẫu nhiên.
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ★ B ĐÃ được ghi thật, dù A hỏng - và thông báo phải nói ra điều đó. Không nói
        // thì Admin tưởng cả lần bấm Lưu vừa rồi vô ích và sẽ bấm lại lần nữa.
        Assert.Equal(222_000m, (await LaySanPhamAsync(_idB)).Price);
        Assert.Contains("Đã lưu 1 sản phẩm", html, StringComparison.Ordinal);

        // Nêu TÊN dòng bị bỏ qua kèm GIÁ TRỊ HIỆN TẠI của người kia. "Có gì đó đã thay
        // đổi" trên bảng 20 dòng là bắt người dùng tự đi dò.
        Assert.Contains("BỎ QUA", html, StringComparison.Ordinal);
        Assert.Contains("Bulk A", html, StringComparison.Ordinal);
        Assert.Contains("999", html, StringComparison.Ordinal);

        // A giữ nguyên giá của người kia.
        Assert.Equal(999_000m, (await LaySanPhamAsync(_idA)).Price);

        // ★★ Phiên bản MỚI phải có mặt trong form cho CẢ HAI dòng, không chỉ dòng vướng:
        // B vừa được ghi xong nên phiên bản của nó cũng đã đổi. Giữ phiên bản cũ cho B
        // thì lần bấm Lưu tiếp theo báo xung đột ở đúng dòng mà chính người dùng vừa ghi
        // thành công - một vòng lặp không lối ra, và rất khó hiểu.
        var (rvAMoi, rvBMoi) = await LayHaiPhienBanAsync();
        Assert.Contains(Convert.ToBase64String(rvAMoi), html, StringComparison.Ordinal);
        Assert.Contains(Convert.ToBase64String(rvBMoi), html, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(rvA), html, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(rvB), html, StringComparison.Ordinal);

        // Giá người dùng vừa gõ vẫn còn nguyên - họ không phải nhập lại 20 dòng vì lỗi
        // của người khác.
        Assert.Contains("111000", html.Replace(",", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dong_xung_dot_duoc_DANH_DAU_ngay_tren_bang_kem_gia_tri_nguoi_kia()
    {
        using var client = await TaoClientAdminAsync();
        var (rvA, rvB) = await LayHaiPhienBanAsync();

        await SuaTrucTiepAsync(_idA, p => { p.Price = 999_000m; p.Stock = 77; });

        var response = await client.PostFormAsync("/Admin/Product/BulkEdit",
            DungForm(page: 1, (_idA, 111_000m, 1, rvA), (_idB, 222_000m, 2, rvB)));

        var html = await response.Content.ReadAsStringAsync();

        // ★ Dòng A phải được tô, dòng B thì KHÔNG. Chỉ khẳng định "trang có chữ tô vàng
        // ở đâu đó" là không phân biệt được với việc tô nhầm cả bảng.
        var dongA = LayDong(html, _idA);
        var dongB = LayDong(html, _idB);

        Assert.Contains("table-warning", dongA, StringComparison.Ordinal);
        Assert.DoesNotContain("table-warning", dongB, StringComparison.Ordinal);

        // Giá trị của NGƯỜI KIA phải nằm NGAY TRONG dòng đó, cạnh ô nhập - không phải
        // ở một đoạn văn đầu trang mà Admin phải tự đối chiếu.
        Assert.Contains($"data-gia-hien-tai=\"{_idA}\"", dongA, StringComparison.Ordinal);

        // Dấu PHẨY, không phải dấu chấm: MoneyFormat khoá InvariantCulture để cùng một
        // số ra cùng một chuỗi bất kể locale máy chạy. Đây chính là lý do assertion này
        // an toàn khi đổi máy - còn ToString("N0") trần thì không.
        Assert.Contains("999,000", dongA, StringComparison.Ordinal);

        // Kèm nhãn thay vì so chuỗi "77" trần: dòng còn chứa Base64 và các id, mà "77"
        // hoàn toàn có thể xuất hiện trong đó -> assertion xanh vì lý do sai.
        Assert.Contains("Hiện tại: 77", dongA, StringComparison.Ordinal);

        Assert.DoesNotContain("data-gia-hien-tai", dongB, StringComparison.Ordinal);

        // ⚠ TUYỆT ĐỐI không disabled: trình duyệt không gửi input disabled, chỉ số
        // Items[i] đứt quãng, và binder âm thầm bỏ toàn bộ phần còn lại của bảng.
        Assert.DoesNotContain("disabled", dongA, StringComparison.Ordinal);
    }

    [Fact]
    public async Task San_pham_bi_xoa_duoc_danh_dau_KHAC_voi_bi_sua()
    {
        using var client = await TaoClientAdminAsync();
        var (rvA, rvB) = await LayHaiPhienBanAsync();

        await XoaTrucTiepAsync(_idA);

        var response = await client.PostFormAsync("/Admin/Product/BulkEdit",
            DungForm(page: 1, (_idA, 111_000m, 1, rvA), (_idB, 222_000m, 2, rvB)));

        var dongA = LayDong(await response.Content.ReadAsStringAsync(), _idA);

        // Hai tình huống đòi hai hành động khác nhau - bị sửa thì bấm Lưu lần nữa là
        // xong, bị xoá thì bấm bao nhiêu lần cũng vô ích, phải tải lại trang. Dùng
        // chung một màu là nói với Admin rằng chúng giống nhau.
        Assert.Contains("table-danger", dongA, StringComparison.Ordinal);
        Assert.DoesNotContain("table-warning", dongA, StringComparison.Ordinal);
        Assert.Contains("đã bị xoá", dongA, StringComparison.Ordinal);

        // Không hiện "Hiện tại: ... đ" cho sản phẩm không còn tồn tại - đó sẽ là in ra
        // giá 0 đ và tồn kho 0 như thể chúng là sự thật.
        Assert.DoesNotContain("data-gia-hien-tai", dongA, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chi_so_DUT_QUANG_thi_binder_am_tham_bo_phan_con_lai()
    {
        using var client = await TaoClientAdminAsync();
        var (rvA, rvB) = await LayHaiPhienBanAsync();

        // Gửi Items[0] và Items[2], THIẾU Items[1] - đúng hình dạng mà một input
        // `disabled` ở giữa bảng tạo ra (trình duyệt không gửi input disabled).
        var fields = DungForm(page: 1, (_idA, 111_000m, 1, rvA));
        fields["Items[2].Id"] = _idB.ToString();
        fields["Items[2].Price"] = "222000";
        fields["Items[2].Stock"] = "2";
        fields["Items[2].RowVersion"] = Convert.ToBase64String(rvB);

        var response = await client.PostFormAsync("/Admin/Product/BulkEdit", fields);

        // ★ Đây là bằng chứng HÀNH VI cho điều mà View chỉ ghi trong comment.
        //
        // Binder đọc Items[0], không thấy Items[1], và DỪNG - Items[2] bị bỏ hoàn toàn.
        // Không exception, không cảnh báo, và response vẫn là redirect thành công: người
        // dùng được báo đã lưu xong trong khi một dòng chưa từng tới server.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(111_000m, (await LaySanPhamAsync(_idA)).Price);
        Assert.Equal(200_000m, (await LaySanPhamAsync(_idB)).Price);   // KHÔNG đổi

        // Test này không kiểm code của dự án mà kiểm một hành vi của framework, và đó
        // chính là lý do nó đáng tồn tại: nó là thứ biến quy tắc "không được disabled ô
        // nhập" từ một lời dặn trong comment thành một điều đo được.
    }

    [Fact]
    public async Task POST_khong_dang_nhap_Admin_thi_bi_chan()
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var (rvA, _) = await LayHaiPhienBanAsync();

        var response = await client.PostFormAsync("/Admin/Product/BulkEdit",
            DungForm(page: 1, (_idA, 111_000m, 1, rvA)));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(100_000m, (await LaySanPhamAsync(_idA)).Price);
    }

    // ───────────── Helper ─────────────

    /// <summary>
    /// Dựng thẳng các trường form thay vì bóc từ trang GET.
    ///
    /// <para>
    /// Cố ý: thứ tự dòng trên trang phụ thuộc <c>OrderBy(Name)</c> của TOÀN BỘ bảng
    /// Products, mà các test class khác chạy song song vẫn đang thêm/xoá sản phẩm - bóc
    /// từ trang là mời một nguồn đỏ ngẫu nhiên vào. Hợp đồng GET → form đã được
    /// <see cref="ProductBulkEditPageTests"/> khoá riêng; ở đây chỉ kiểm đường POST.
    /// </para>
    /// </summary>
    private static Dictionary<string, string> DungForm(
        int page,
        params (int Id, decimal Price, int Stock, byte[]? RowVersion)[] dong)
    {
        var fields = new Dictionary<string, string> { ["Page"] = page.ToString() };

        for (var i = 0; i < dong.Length; i++)
        {
            fields[$"Items[{i}].Id"] = dong[i].Id.ToString();

            // InvariantCulture: query/form được bind bằng InvariantCulture, còn
            // ToString() không tham số dùng CurrentCulture. Máy vi-VN sẽ gửi "111000,00"
            // và binder trả về 0 trong im lặng.
            fields[$"Items[{i}].Price"] = dong[i].Price.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            fields[$"Items[{i}].Stock"] = dong[i].Stock.ToString();
            fields[$"Items[{i}].RowVersion"] =
                dong[i].RowVersion is null ? string.Empty : Convert.ToBase64String(dong[i].RowVersion!);
        }

        return fields;
    }

    /// <summary>
    /// Bóc đúng thẻ <c>&lt;tr&gt;</c> chứa sản phẩm có Id cho trước.
    ///
    /// <para>
    /// Cần thiết vì mọi assertion về "dòng nào được tô" chỉ có nghĩa khi xét TRONG PHẠM
    /// VI một dòng. Khẳng định <c>html.Contains("table-warning")</c> vẫn xanh y hệt khi
    /// code tô nhầm cả bảng - tức nó không kiểm chứng được đúng cái đang cần kiểm.
    /// </para>
    /// <para>
    /// Cắt theo <c>&lt;tr</c> chứ không regex một phát từ <c>&lt;tr&gt;</c> tới
    /// <c>&lt;/tr&gt;</c>: bên trong dòng còn có thẻ khác và regex tham lam/lười đều dễ
    /// bắt sai biên. Kèm <c>Assert</c> cho chính helper để nó tự tố giác khi không tìm
    /// thấy dòng, thay vì trả chuỗi rỗng và làm test đỏ ở một assertion chẳng liên quan.
    /// </para>
    /// </summary>
    private static string LayDong(string html, int productId)
    {
        var dong = html
            .Split("<tr", StringSplitOptions.None)
            .FirstOrDefault(d => d.Contains($"value=\"{productId}\"", StringComparison.Ordinal));

        Assert.NotNull(dong);

        return dong;
    }

    private async Task<(byte[] A, byte[] B)> LayHaiPhienBanAsync() =>
        ((await LaySanPhamAsync(_idA)).RowVersion!, (await LaySanPhamAsync(_idB)).RowVersion!);

    private async Task<Product> LaySanPhamAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        return await context.Products.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    /// <summary>"Người khác" - scope riêng, DbContext riêng, RowVersion tăng thật.</summary>
    private async Task SuaTrucTiepAsync(int id, Action<Product> sua)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var product = await context.Products.SingleAsync(p => p.Id == id);
        sua(product);
        await context.SaveChangesAsync();
    }

    private async Task XoaTrucTiepAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products.Where(p => p.Id == id).ExecuteDeleteAsync();
    }

    private async Task<T> TrongScopeAsync<T>(Func<IProductService, Task<T>> viec)
    {
        using var scope = _factory.Services.CreateScope();

        return await viec(scope.ServiceProvider.GetRequiredService<IProductService>());
    }

    private async Task<HttpClient> TaoClientAdminAsync()
    {
        var (client, username) = await _factory.TaoClientAdminAsync("bu");

        _usernames.Add(username);

        return client;
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        await context.Products.Where(p => p.CategoryId == _categoryId).ExecuteDeleteAsync();
        await context.Categories.Where(c => c.Id == _categoryId).ExecuteDeleteAsync();
        await context.Users.Where(u => _usernames.Contains(u.Username)).ExecuteDeleteAsync();

        _factory.Dispose();
    }
}
