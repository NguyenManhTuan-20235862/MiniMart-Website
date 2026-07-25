using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Web.Areas.Admin.Models;
using MiniMart.Web.Services;

namespace MiniMart.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class ProductController : Controller
{
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly IProductImageStorage _imageStorage;

    public ProductController(
        IProductService productService,
        ICategoryService categoryService,
        IProductImageStorage imageStorage)
    {
        _productService = productService;
        _categoryService = categoryService;
        _imageStorage = imageStorage;
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

        // Lưu file TRƯỚC khi gọi Service: nếu Service ném exception, file thừa
        // nằm lại trên đĩa nhưng không có bản ghi nào trỏ tới - chấp nhận được.
        // Ngược lại (lưu DB trước) sẽ có bản ghi trỏ tới file không tồn tại.
        var imageUrl = await LuuAnhNeuCoAsync(model.ImageFile, cancellationToken);

        try
        {
            await _productService.CreateAsync(
                model.Name, model.Price, model.Stock, model.CategoryId, imageUrl, cancellationToken);
        }
        catch (NotFoundException ex) when (ex.EntityName == nameof(Category))
        {
            // Danh mục bị xoá sau khi form đã render - lỗi gắn vào đúng ô chọn
            // danh mục để người dùng chọn lại.
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
            ExistingImageUrl = product.ImageUrl,
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

        // null = không chọn ảnh mới -> Service giữ nguyên ảnh cũ.
        var imageUrl = await LuuAnhNeuCoAsync(model.ImageFile, cancellationToken);

        try
        {
            await _productService.UpdateAsync(
                id, model.Name, model.Price, model.Stock, model.CategoryId, imageUrl, cancellationToken);

            // Thay ảnh thành công thì dọn file cũ, tránh rác tích tụ trong wwwroot.
            if (imageUrl is not null && model.ExistingImageUrl != imageUrl)
            {
                _imageStorage.Delete(model.ExistingImageUrl);
            }
        }
        catch (NotFoundException ex) when (ex.EntityName == nameof(Category))
        {
            ModelState.AddModelError(nameof(model.CategoryId), ex.Message);
            model.Categories = await LayDanhSachDanhMucAsync(cancellationToken);
            return View(model);
        }
        catch (NotFoundException)
        {
            // Không tìm thấy chính SẢN PHẨM đang sửa -> render lại form là vô
            // nghĩa, phải trả 404.
            return NotFound();
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
        // Lấy đường dẫn ảnh TRƯỚC khi xoá bản ghi, sau đó không còn tra được nữa.
        var product = await _productService.GetByIdAsync(id, cancellationToken);

        try
        {
            await _productService.DeleteAsync(id, cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        // Chỉ xoá file khi DB đã xoá xong. Làm ngược lại mà DB lỗi thì bản ghi
        // còn nguyên nhưng ảnh đã mất.
        _imageStorage.Delete(product?.ImageUrl);

        TempData["Success"] = "Đã xoá sản phẩm.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Trả về null khi người dùng không chọn file — Service hiểu là giữ ảnh cũ.</summary>
    private async Task<string?> LuuAnhNeuCoAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        return await _imageStorage.SaveAsync(file, cancellationToken);
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
