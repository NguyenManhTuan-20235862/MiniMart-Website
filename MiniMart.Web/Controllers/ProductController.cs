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

    /// <summary>
    /// Bốn gợi ý: vừa đủ một hàng trên desktop (row-cols-lg-4) nên không có dòng
    /// thứ hai lẻ loi ở mọi kích thước màn hình.
    /// </summary>
    private const int SoSanPhamLienQuan = 4;

    /// <summary>Số trang tiếp theo; rỗng khi đã hết dữ liệu.</summary>
    public const string NextPageHeader = "X-Next-Page";

    private readonly IProductService _productService;

    public ProductController(IProductService productService)
    {
        _productService = productService;
    }

    /// <summary>Số dòng tối đa trong dropdown gợi ý.</summary>
    private const int SoGoiY = 8;

    /// <summary>
    /// Gợi ý cho ô tìm kiếm ở header, trả về HTML của danh sách gợi ý.
    ///
    /// <para>
    /// <b>PartialView chứ không Json</b>, theo quy ước mặc định của dự án — và ở đây
    /// nó đặc biệt đúng: thứ được trả về là TÊN SẢN PHẨM, và nó sẽ được nhét thẳng vào
    /// DOM. Với Razor thì việc escape xảy ra ở server và không thể quên; với JSON thì
    /// JSON <b>không</b> escape ký tự <c>&lt;</c>, nên trách nhiệm escape rơi vào
    /// JavaScript và quên một trường là XSS. Đổi lại, markup dòng gợi ý chỉ tồn tại
    /// một chỗ duy nhất và <b>kiểm được bằng integration test</b>.
    /// </para>
    /// <para>
    /// Không <c>[Authorize]</c>: tìm kiếm là việc của mọi người.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Suggest(
        string? tuKhoa,
        CancellationToken cancellationToken = default)
    {
        // Từ khoá quá ngắn -> Service trả rỗng -> partial render ra chuỗi rỗng. Đó là
        // kết cục BÌNH THƯỜNG, không phải lỗi, nên vẫn là 200.
        var goiY = await _productService.SuggestAsync(tuKhoa, SoGoiY, cancellationToken);

        return PartialView("_ProductSuggestions", goiY);
    }

    /// <summary>
    /// Trang chi tiết một sản phẩm: <c>/Product/Details/5</c>.
    ///
    /// <para>
    /// Giữ route MẶC ĐỊNH thay vì gắn <c>[HttpGet("Product/{id:int}")]</c> cho URL
    /// ngắn <c>/Product/5</c>. Lý do không phải gu: attribute routing trên một action
    /// sẽ TẮT route mặc định cho chính action đó, nên <c>/Product/Details/5</c> chết
    /// theo — trong khi <c>/Order/Details/3</c> đang dùng đúng dạng dài. Một dự án hai
    /// kiểu URL tốn nhiều hơn cái URL ngắn mang lại.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Không <c>[Authorize]</c>: xem hàng là việc của mọi người, kể cả khách vãng lai.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetByIdAsync(id, cancellationToken);

        if (product is null)
        {
            // 404 chứ không phải trang trống kèm 200: id sai là "không có thứ này",
            // và trả 200 cho một trang rỗng khiến công cụ tìm kiếm lập chỉ mục nó.
            return NotFound();
        }

        // ★ Hai await NỐI TIẾP, tuyệt đối không Task.WhenAll.
        //
        // Ở đây có tới hai lý do độc lập, và lý do thứ hai mới là cái không thể lách:
        //   1. Hai lệnh dùng chung một DbContext (Scoped) mà DbContext KHÔNG
        //      thread-safe -> "A second operation was started on this context instance".
        //   2. Lệnh thứ hai cần product.CategoryId của lệnh thứ nhất. Chúng phụ thuộc
        //      dữ liệu, nên song song hoá là vô nghĩa ngay từ đầu.
        //
        // Đặt SAU lệnh kiểm null cũng có chủ ý: id không tồn tại thì không tốn một
        // round-trip nào để đi tìm sản phẩm liên quan của một danh mục không có.
        var lienQuan = await _productService.GetRelatedAsync(
            product.Id, product.CategoryId, SoSanPhamLienQuan, cancellationToken);

        return View(new ProductDetailViewModel
        {
            Product = product,
            SanPhamLienQuan = lienQuan
        });
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
            search: filter.Search,
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
