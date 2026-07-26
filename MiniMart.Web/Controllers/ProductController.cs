using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using MiniMart.Application.Interfaces;
using MiniMart.Web.Models;

namespace MiniMart.Web.Controllers;

/// <summary>
/// Sản phẩm phía KHÁCH HÀNG, không nằm trong Area Admin nên không có
/// [Authorize] - ai cũng xem được. Trùng tên class với
/// Areas/Admin/Controllers/ProductController nhưng khác namespace và khác
/// route (/Product vs /Admin/Product); đó chính là việc Area sinh ra để giải quyết.
/// </summary>
public class ProductController : Controller
{
    private const int PageSize = 12;

    /// <summary>Số trang tiếp theo; rỗng khi đã hết dữ liệu.</summary>
    public const string NextPageHeader = "X-Next-Page";

    private readonly IProductService _productService;

    public ProductController(IProductService productService)
    {
        _productService = productService;
    }

    /// <summary>
    /// Trả về HTML của một trang thẻ sản phẩm cho nút "Xem thêm".
    ///
    /// <para>
    /// Dùng PartialView chứ KHÔNG dùng Json: markup thẻ chỉ được định nghĩa
    /// một lần duy nhất ở _ProductCard.cshtml. Trả JSON thì client phải dựng
    /// lại markup bằng JavaScript, tức viết lần thứ hai cùng một giao diện,
    /// cùng một cách định dạng tiền, cùng một logic badge còn/hết hàng - và
    /// phải tự escape XSS bằng tay vì JSON không escape ký tự &lt;.
    /// </para>
    /// <para>
    /// KHÔNG dùng View(): View() gửi lại cả layout (thẻ html, navbar, footer),
    /// trong khi trình duyệt đã có sẵn trang rồi.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Bộ tham số lọc phải TRÙNG KHỚP với HomeController.Index. Trang 1 do
    /// server render, trang 2+ do action này trả về - lệch một tham số là
    /// người dùng bấm "Xem thêm" xong nhận về sản phẩm ngoài bộ lọc.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> LoadMore(
        ProductFilter filter,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        // page/pageSize đã được Repository kẹp lại, nên ?page=-5 không làm vỡ SQL.
        var result = await _productService.GetProductsAsync(
            categoryId: filter.CategoryId,
            minPrice: filter.MinPrice,
            maxPrice: filter.MaxPrice,
            page: page,
            pageSize: PageSize,
            cancellationToken: cancellationToken);

        // Body là HTML nên không có chỗ đặt metadata phân trang. Đưa qua header:
        // giữ body sạch để dán thẳng vào DOM, mà client vẫn biết còn trang sau
        // hay không. Thiếu thông tin này thì nút "Xem thêm" gọi mãi không dừng.
        //
        // Hết dữ liệu -> gửi header với giá trị RỖNG (đã kiểm chứng bằng curl:
        // Kestrel vẫn gửi "X-Next-Page: " chứ không bỏ header đi). Phía JS dùng
        // `if (trangKe)` nên rỗng và vắng mặt được xử lý như nhau - không phụ
        // thuộc vào chi tiết cài đặt này của framework.
        //
        // Header chỉ đọc được vì cùng origin; nếu sau này gọi cross-origin thì
        // phải thêm Access-Control-Expose-Headers.
        Response.Headers[NextPageHeader] = result.HasNextPage
            ? (result.Page + 1).ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        return PartialView("_ProductCards", result.Items);
    }
}
