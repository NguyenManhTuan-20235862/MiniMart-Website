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

    private readonly IProductService _productService;

    public ProductController(IProductService productService)
    {
        _productService = productService;
    }

    /// <summary>
    /// Trả JSON cho nút "Xem thêm" / cuộn vô tận. Không dùng View() vì trình
    /// duyệt đã có sẵn trang rồi - chỉ cần thêm dữ liệu, không cần lại layout.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> LoadMore(
        int? categoryId,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        // page/pageSize đã được Repository kẹp lại, nên ?page=-5 không làm vỡ SQL.
        var result = await _productService.GetProductsAsync(
            categoryId: categoryId,
            page: page,
            pageSize: PageSize,
            cancellationToken: cancellationToken);

        var items = result.Items
            .Select(p => new ProductListItemDto(p.Id, p.Name, p.Price, p.ImageUrl))
            .ToList();

        return Json(new LoadMoreResponse(items, result.Page, result.HasNextPage));
    }
}
