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
    /// Ba tham số lọc đều nullable và đều có thể vắng mặt độc lập. Controller
    /// KHÔNG viết if/else để chọn "kiểu truy vấn" - nó chỉ chuyển nguyên trạng
    /// xuống Service; việc bỏ bớt điều kiện khi giá trị null là của Repository.
    /// </summary>
    public async Task<IActionResult> Index(
        int? categoryId,
        decimal? minPrice,
        decimal? maxPrice,
        CancellationToken cancellationToken)
    {
        // Hai lệnh await NỐI TIẾP nhau, không gói vào Task.WhenAll.
        // Cả hai dùng chung một DbContext (Scoped trong cùng request), mà
        // DbContext KHÔNG thread-safe: chạy song song sẽ ném
        // "A second operation was started on this context instance".
        var products = await _productService.GetProductsAsync(
            categoryId: categoryId,
            minPrice: minPrice,
            maxPrice: maxPrice,
            page: 1,
            pageSize: PageSize,
            cancellationToken: cancellationToken);

        var categories = await _categoryService.GetAllAsync(cancellationToken);

        return View(new HomeIndexViewModel
        {
            Products = products,
            Categories = categories,
            SelectedCategoryId = categoryId,
            MinPrice = minPrice,
            MaxPrice = maxPrice
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
