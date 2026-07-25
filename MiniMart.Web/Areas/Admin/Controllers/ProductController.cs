using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Web.Areas.Admin.Models;

namespace MiniMart.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class ProductController : Controller
{
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;

    public ProductController(IProductService productService, ICategoryService categoryService)
    {
        _productService = productService;
        _categoryService = categoryService;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var products = await _productService.GetAllAsync(cancellationToken);
        return View(products);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        return View(new ProductFormViewModel
        {
            Categories = await LayDanhSachDanhMucAsync(cancellationToken)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            // Dropdown KHÔNG được gửi lên trong POST, phải nạp lại trước khi
            // render lại form - bỏ sót bước này là lỗi kinh điển của MVC.
            model.Categories = await LayDanhSachDanhMucAsync(cancellationToken);
            return View(model);
        }

        try
        {
            await _productService.CreateAsync(
                model.Name, model.Price, model.Stock, model.CategoryId, cancellationToken);
        }
        catch (NotFoundException ex)
        {
            ModelState.AddModelError(nameof(model.CategoryId), ex.Message);
            model.Categories = await LayDanhSachDanhMucAsync(cancellationToken);
            return View(model);
        }

        TempData["Success"] = $"Đã thêm sản phẩm '{model.Name}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetByIdAsync(id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        return View(new ProductFormViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            Stock = product.Stock,
            CategoryId = product.CategoryId,
            Categories = await LayDanhSachDanhMucAsync(cancellationToken)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Categories = await LayDanhSachDanhMucAsync(cancellationToken);
            return View(model);
        }

        try
        {
            await _productService.UpdateAsync(
                id, model.Name, model.Price, model.Stock, model.CategoryId, cancellationToken);
        }
        catch (NotFoundException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            model.Categories = await LayDanhSachDanhMucAsync(cancellationToken);
            return View(model);
        }

        TempData["Success"] = $"Đã cập nhật sản phẩm '{model.Name}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetByIdAsync(id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        return View(product);
    }

    [HttpPost, ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _productService.DeleteAsync(id, cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        TempData["Success"] = "Đã xoá sản phẩm.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IEnumerable<SelectListItem>> LayDanhSachDanhMucAsync(
        CancellationToken cancellationToken)
    {
        var categories = await _categoryService.GetAllAsync(cancellationToken);

        return categories.Select(c => new SelectListItem
        {
            Value = c.Id.ToString(),
            Text = c.Name
        });
    }
}
