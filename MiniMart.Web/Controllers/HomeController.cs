using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MiniMart.Application.Interfaces;
using MiniMart.Web.Models;

namespace MiniMart.Web.Controllers;

public class HomeController : Controller
{
    private const int PageSize = 12;

    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;

    public HomeController(IProductService productService, ICategoryService categoryService)
    {
        _productService = productService;
        _categoryService = categoryService;
    }

    /// <summary>
    /// Nhận ProductFilter thay vì 3 tham số rời: cùng một type với
    /// ProductController.LoadMore nên hai bên không thể lệch bộ lọc.
    ///
    /// <para>
    /// ModelState có thể KHÔNG hợp lệ (giá âm, khoảng giá ngược) nhưng vẫn truy
    /// vấn bình thường: đây là trang xem hàng, không phải form ghi dữ liệu. Chặn
    /// hẳn thì người dùng chỉ thấy trang trống. Thông báo lỗi được view hiện
    /// qua validation summary, còn kết quả (rỗng) vẫn đúng với điều kiện đã gửi.
    /// </para>
    /// </summary>
    public async Task<IActionResult> Index(ProductFilter filter, CancellationToken cancellationToken)
    {
        // Hai lệnh await NỐI TIẾP nhau, không gói vào Task.WhenAll.
        // Cả hai dùng chung một DbContext (Scoped trong cùng request), mà
        // DbContext KHÔNG thread-safe: chạy song song sẽ ném
        // "A second operation was started on this context instance".
        var products = await _productService.GetProductsAsync(
            categoryId: filter.CategoryId,
            minPrice: filter.MinPrice,
            maxPrice: filter.MaxPrice,
            page: 1,
            pageSize: PageSize,
            cancellationToken: cancellationToken);

        var categories = await _categoryService.GetAllAsync(cancellationToken);

        return View(new HomeIndexViewModel
        {
            Products = products,
            Categories = categories,
            Filter = filter
        });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
