using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Tests;

/// <summary>
/// Optimistic Concurrency cho luồng sửa sản phẩm.
///
/// <para>
/// BẮT BUỘC chạy trên SQL Server thật. EF Core InMemory KHÔNG thực thi
/// concurrency token nên mọi test ở đây sẽ xanh kể cả khi RowVersion không hề
/// được round-trip - tức tệ hơn là không có test.
/// </para>
/// <para>
/// Mỗi "người dùng" là một DI scope riêng, vì mỗi scope có một DbContext riêng
/// với Change Tracker riêng. Dùng chung một scope thì hai bên nhìn thấy cùng một
/// entity trong bộ nhớ và không có xung đột nào xảy ra.
/// </para>
/// </summary>
public class ProductConcurrencyTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<string> _usernames = [];

    private int _categoryId;
    private int _productId;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        var category = new Category { Name = $"CC_{Guid.NewGuid():N}"[..14] };
        var product = new Product
        {
            Name = "Ban dau",
            Price = 100_000m,
            Stock = 10,
            Category = category
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        _categoryId = category.Id;
        _productId = product.Id;
    }

    // ───────────── Tầng Service ─────────────

    [Fact]
    public async Task Sua_voi_RowVersion_cu_thi_nem_ConcurrencyConflictException()
    {
        var rowVersionCu = await LayRowVersionAsync();

        // "Người khác" lưu trước -> SQL Server tự tăng RowVersion.
        await TrongScopeAsync(svc => svc.UpdateAsync(
            _productId, "Nguoi khac sua", 500_000m, 5, _categoryId));

        // Ta lưu sau, mang theo phiên bản đã thấy lúc mở form.
        using var scope = _factory.Services.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            productService.UpdateAsync(
                _productId, "Ta sua", 111_000m, 1, _categoryId, rowVersion: rowVersionCu));

        // Thay đổi của người khác phải CÒN NGUYÊN. Đây là phần quan trọng nhất:
        // không ném exception thì "Ta sua" đã ghi đè và công của họ mất không dấu vết.
        var hienTai = await LaySanPhamAsync();
        Assert.Equal("Nguoi khac sua", hienTai!.Name);
        Assert.Equal(500_000m, hienTai.Price);
    }

    [Fact]
    public async Task Sua_voi_RowVersion_dung_thi_luu_binh_thuong()
    {
        var rowVersion = await LayRowVersionAsync();

        await TrongScopeAsync(svc => svc.UpdateAsync(
            _productId, "Da sua", 222_000m, 7, _categoryId, rowVersion: rowVersion));

        var hienTai = await LaySanPhamAsync();
        Assert.Equal("Da sua", hienTai!.Name);
    }

    [Fact]
    public async Task RowVersion_null_thi_bo_qua_kiem_tra_xung_dot()
    {
        // Luồng nội bộ không có form (job, seed dữ liệu) không có phiên bản nào
        // để gửi lên. Bỏ qua kiểm tra là chủ ý, không phải quên.
        await TrongScopeAsync(svc => svc.UpdateAsync(
            _productId, "Nguoi khac sua", 500_000m, 5, _categoryId));

        await TrongScopeAsync(svc => svc.UpdateAsync(
            _productId, "Job sua", 1_000m, 1, _categoryId, rowVersion: null));

        Assert.Equal("Job sua", (await LaySanPhamAsync())!.Name);
    }

    [Fact]
    public async Task Ban_ghi_bi_xoa_cung_nem_ConcurrencyConflictException()
    {
        var rowVersion = await LayRowVersionAsync();

        await TrongScopeAsync(svc => svc.DeleteAsync(_productId));

        using var scope = _factory.Services.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

        // Xoá rồi thì GetForUpdateAsync trả null trước khi tới SaveChanges, nên
        // đây là NotFoundException chứ không phải xung đột - Controller phân biệt
        // hai trường hợp này bằng cách bắt riêng từng loại.
        await Assert.ThrowsAsync<NotFoundException>(() =>
            productService.UpdateAsync(
                _productId, "Ta sua", 111_000m, 1, _categoryId, rowVersion: rowVersion));
    }

    // ───────────── Tầng HTTP ─────────────

    [Fact]
    public async Task Form_Edit_render_RowVersion_dang_Base64()
    {
        using var client = await TaoClientAdminAsync();

        var html = await client.GetStringAsync($"/Admin/Product/Edit/{_productId}");
        var rowVersion = LayHiddenValue(html, "RowVersion");

        Assert.NotEmpty(rowVersion);

        // asp-for trên byte[] gọi ToString() và cho ra "System.Byte[]" - form vẫn
        // submit bình thường nhưng model binder không giải mã được, tức tính năng
        // biến mất trong im lặng.
        Assert.DoesNotContain("System.Byte", html);

        // Phải giải mã được thành đúng 8 byte của cột rowversion SQL Server.
        Assert.Equal(8, Convert.FromBase64String(rowVersion).Length);
    }

    [Fact]
    public async Task POST_voi_RowVersion_cu_thi_hien_thong_bao_xung_dot_va_KHONG_luu()
    {
        using var client = await TaoClientAdminAsync();

        var formHtml = await client.GetStringAsync($"/Admin/Product/Edit/{_productId}");
        var truong = DocFormFields(formHtml);

        // Người khác lưu trong lúc "form đang mở".
        await TrongScopeAsync(svc => svc.UpdateAsync(
            _productId, "Nguoi khac sua", 500_000m, 5, _categoryId));

        truong["Name"] = "Ta sua";
        truong["Price"] = "111000";

        var response = await client.PostAsync(
            $"/Admin/Product/Edit/{_productId}", new FormUrlEncodedContent(truong));
        var html = await response.Content.ReadAsStringAsync();

        // 200 = render lại form. Redirect nghĩa là đã lưu -> ghi đè mất dữ liệu.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Người khác đã sửa sản phẩm này", html);

        // Thông báo phải nêu giá trị hiện tại, nếu không người dùng không biết
        // mình đang ghi đè lên cái gì.
        Assert.Contains("Nguoi khac sua", html);

        Assert.Equal("Nguoi khac sua", (await LaySanPhamAsync())!.Name);
    }

    [Fact]
    public async Task Sau_xung_dot_bam_Luu_lan_hai_thi_thanh_cong()
    {
        using var client = await TaoClientAdminAsync();

        var formHtml = await client.GetStringAsync($"/Admin/Product/Edit/{_productId}");
        var truong = DocFormFields(formHtml);

        await TrongScopeAsync(svc => svc.UpdateAsync(
            _productId, "Nguoi khac sua", 500_000m, 5, _categoryId));

        truong["Name"] = "Ta sua";

        var lanMot = await client.PostAsync(
            $"/Admin/Product/Edit/{_productId}", new FormUrlEncodedContent(truong));
        var htmlXungDot = await lanMot.Content.ReadAsStringAsync();

        // Form render lại phải mang RowVersion MỚI. Nếu vẫn là phiên bản cũ thì
        // người dùng mắc kẹt: bấm Lưu bao nhiêu lần cũng xung đột, không có
        // đường nào ra ngoài việc mở lại trang từ đầu.
        var truongLanHai = DocFormFields(htmlXungDot);
        Assert.NotEqual(truong["RowVersion"], truongLanHai["RowVersion"]);

        truongLanHai["Name"] = "Ta sua lan hai";

        var lanHai = await client.PostAsync(
            $"/Admin/Product/Edit/{_productId}", new FormUrlEncodedContent(truongLanHai));

        Assert.Equal(HttpStatusCode.Redirect, lanHai.StatusCode);
        Assert.Equal("Ta sua lan hai", (await LaySanPhamAsync())!.Name);
    }

    // ───────────── Helper ─────────────

    private async Task<byte[]> LayRowVersionAsync() => (await LaySanPhamAsync())!.RowVersion;

    private async Task<Product?> LaySanPhamAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();

        return await context.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == _productId);
    }

    private async Task TrongScopeAsync(Func<IProductService, Task> hanhDong)
    {
        using var scope = _factory.Services.CreateScope();
        await hanhDong(scope.ServiceProvider.GetRequiredService<IProductService>());
    }

    /// <summary>
    /// Đọc mọi input hidden/text/number của form thành dictionary để POST lại
    /// đúng như trình duyệt làm - gồm cả antiforgery token và RowVersion.
    /// </summary>
    private static Dictionary<string, string> DocFormFields(string html)
    {
        var truong = new Dictionary<string, string>();

        foreach (var match in Regex.Matches(html, "<input[^>]*>").Cast<Match>())
        {
            var the = match.Value;
            var ten = Regex.Match(the, """name="([^"]+)""").Groups[1].Value;

            if (string.IsNullOrEmpty(ten) || the.Contains("type=\"file\""))
            {
                continue;
            }

            // ★ BẮT BUỘC HtmlDecode, và đây là bug đã làm cả bộ test đỏ NGẪU NHIÊN.
            //
            // Base64 của rowversion đôi khi chứa ký tự '+', mà HtmlEncoder mã hoá nó
            // thành "&#x2B;". Trình duyệt tự giải mã khi parse thuộc tính nên form thật
            // gửi đi đúng chuỗi; helper này bóc chuỗi THÔ rồi POST lại nguyên xi, nên
            // binder không giải mã được Base64, RowVersion về null, SetExpectedRowVersion
            // bị bỏ qua, KHÔNG xung đột nào được phát hiện, và controller redirect thay
            // vì render lại. Test đỏ ở assertion "Expected: OK / Actual: Found" - không
            // một manh mối nào chỉ về dấu '+'.
            //
            // Chỉ nổ khi giá trị rowversion ngẫu nhiên sinh ra dấu '+', nên nó trông
            // hệt như một test flaky do hạ tầng. Không phải: nó là bug thật của test.
            truong[ten] = WebUtility.HtmlDecode(
                Regex.Match(the, """value="([^"]*)""").Groups[1].Value);
        }

        // <select> không phải <input> nên vòng lặp trên bỏ sót.
        //
        // Tìm thẻ <option> có selected rồi mới bóc value ra, KHÔNG khớp
        // """<option value="..." selected""" trong một lần: SelectTagHelper của
        // asp-items đặt selected TRƯỚC value, nên regex phụ thuộc thứ tự thuộc
        // tính sẽ trượt và CategoryId thành rỗng - lúc đó ModelState invalid và
        // request không bao giờ tới được nhánh xử lý xung đột.
        var optionDangChon = Regex.Match(html, "<option[^>]*selected[^>]*>");
        truong["CategoryId"] = Regex.Match(optionDangChon.Value, """value="([^"]*)""")
            .Groups[1].Value;

        Assert.True(
            truong["CategoryId"].Length > 0,
            "Không đọc được CategoryId đang chọn từ form - helper đọc form đã sai.");

        return truong;
    }

    private static string LayHiddenValue(string html, string name)
    {
        var match = Regex.Match(html, $$"""<input[^>]*name="{{name}}"[^>]*value="([^"]*)"([^>]*)>""");

        // HtmlDecode vì Base64 có thể chứa '+' và HtmlEncoder mã hoá nó thành "&#x2B;".
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<HttpClient> TaoClientAdminAsync()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var username = $"cc_{Guid.NewGuid():N}"[..16];
        const string password = "MatKhau123";

        _usernames.Add(username);

        await PostFormAsync(client, "/Account/Register", new()
        {
            ["Username"] = username,
            ["Password"] = password,
            ["ConfirmPassword"] = password
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MiniMartDbContext>();
            var user = await context.Users.SingleAsync(u => u.Username == username);
            user.Role = UserRole.Admin;
            await context.SaveChangesAsync();
        }

        // Đăng nhập LẠI sau khi nâng quyền: claims là ảnh chụp lúc đăng nhập.
        var dangNhap = await PostFormAsync(client, "/Account/Login", new()
        {
            ["Username"] = username,
            ["Password"] = password,
            ["RememberMe"] = "false"
        });

        // ★ Khẳng định đăng nhập THÀNH CÔNG ngay tại đây, đừng để test tự phát hiện
        // qua hệ quả.
        //
        // Lý do rất cụ thể: /Account/Login là endpoint DUY NHẤT có rate limit, và
        // limiter phân vùng theo IP - mà mọi client của WebApplicationFactory đều có
        // RemoteIpAddress = null, tức CẢ BỘ TEST dùng chung một hạn mức. Khi nó bị
        // vượt, login trả 429, client không có cookie, request tới /Admin/... bị đá về
        // trang đăng nhập, và test đỏ ở một assertion nói về Base64 RowVersion - không
        // có manh mối nào chỉ về rate limit.
        //
        // Đã gặp thật: hai test ở file này đỏ trong một lần chạy toàn bộ rồi xanh lại
        // ở lần sau, và xanh khi chạy riêng.
        Assert.True(
            dangNhap.StatusCode is HttpStatusCode.Found or HttpStatusCode.OK,
            $"Đăng nhập thất bại với {(int)dangNhap.StatusCode}. "
            + "429 nghĩa là bộ test đã vượt RateLimiting:LoginPermitLimit (dùng chung "
            + "một hạn mức vì RemoteIpAddress luôn null trong WebApplicationFactory).");

        return client;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string path, Dictionary<string, string> fields)
    {
        var form = await client.GetStringAsync(path);
        fields["__RequestVerificationToken"] =
            Regex.Match(form, """name="__RequestVerificationToken"[^>]*value="([^"]+)""").Groups[1].Value;

        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
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
