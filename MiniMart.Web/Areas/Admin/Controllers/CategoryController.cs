using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Web.Areas.Admin.Models;

namespace MiniMart.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class CategoryController : Controller
{
    private readonly ICategoryService _categoryService;

    // Chỉ inject ICategoryService. Controller không biết ICategoryRepository
    // hay DbContext tồn tại - đúng quy ước trong CLAUDE.md.
    public CategoryController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var categories = await _categoryService.GetAllAsync(cancellationToken);
        return View(categories);
    }

    [HttpGet]
    public IActionResult Create() => View(new CategoryFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _categoryService.CreateAsync(model.Name, cancellationToken);
        }
        catch (CategoryNameAlreadyExistsException ex)
        {
            // Toàn bộ việc của Controller khi gặp lỗi nghiệp vụ: dịch exception
            // thành thông báo trên form. Không có logic quyết định nào ở đây.
            ModelState.AddModelError(nameof(model.Name), ex.Message);
            return View(model);
        }

        TempData["Success"] = $"Đã thêm danh mục '{model.Name}'.";

        // Post-Redirect-Get: trả về redirect thay vì render thẳng, để người dùng
        // bấm F5 không gửi lại form lần nữa.
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var category = await _categoryService.GetByIdAsync(id, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        return View(new CategoryFormViewModel { Id = category.Id, Name = category.Name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _categoryService.UpdateAsync(id, model.Name, cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (CategoryNameAlreadyExistsException ex)
        {
            ModelState.AddModelError(nameof(model.Name), ex.Message);
            return View(model);
        }

        TempData["Success"] = $"Đã cập nhật danh mục '{model.Name}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var category = await _categoryService.GetByIdAsync(id, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        return View(category);
    }

    // Tên khác action GET vì cùng chữ ký sẽ trùng overload; ActionName đưa nó
    // về đúng route /Admin/Category/Delete.
    [HttpPost, ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _categoryService.DeleteAsync(id, cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (CategoryHasProductsException ex)
        {
            // Quy tắc nghiệp vụ nằm ở Service; Controller chỉ hiển thị kết quả.
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = "Đã xoá danh mục.";
        return RedirectToAction(nameof(Index));
    }
}
