using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Web.Areas.Admin.Models;
using MiniMart.Web.Extensions;
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
            // Chụp lại phiên bản NGAY LÚC NÀY. Đây là mốc mà lần lưu tới sẽ so với.
            RowVersion = product.RowVersion,
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
                id, model.Name, model.Price, model.Stock, model.CategoryId, imageUrl,
                model.RowVersion, cancellationToken);

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
        catch (ConcurrencyConflictException)
        {
            // Người khác đã sửa (hoặc xoá) bản ghi này trong lúc form đang mở.
            //
            // KHÔNG tự quyết định ghi đè: mỗi bên có thể đã sửa những trường khác
            // nhau, ghi đè âm thầm là làm mất công của người kia (lost update) -
            // đúng thứ RowVersion sinh ra để chặn. Cũng KHÔNG huỷ luôn dữ liệu
            // người dùng vừa nhập. Việc đúng là: cho họ thấy giá trị hiện tại,
            // giữ nguyên những gì họ đã điền, rồi để họ chọn.
            return await RenderLaiFormXungDotAsync(id, model, cancellationToken);
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
        catch (ProductHasOrdersException ex)
        {
            // Hệ quả cố ý của OrderDetails.ProductId = Restrict. Không phải lỗi hệ
            // thống mà là quy tắc nghiệp vụ, nên trả về danh sách kèm lời giải thích
            // và HƯỚNG XỬ LÝ (đặt tồn kho về 0), không phải trang lỗi.
            //
            // Return sớm ở đây còn giữ được file ảnh: xoá ảnh của một sản phẩm vẫn
            // còn trong DB là làm hỏng mọi đơn hàng cũ đang hiển thị nó.
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }

        // Chỉ xoá file khi DB đã xoá xong. Làm ngược lại mà DB lỗi thì bản ghi
        // còn nguyên nhưng ảnh đã mất.
        _imageStorage.Delete(product?.ImageUrl);

        TempData["Success"] = "Đã xoá sản phẩm.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Render lại form Edit sau xung đột: nêu rõ giá trị hiện tại trong DB và nạp
    /// RowVersion MỚI để lần bấm Lưu tiếp theo có thể thành công.
    /// </summary>
    private async Task<IActionResult> RenderLaiFormXungDotAsync(
        int id,
        ProductFormViewModel model,
        CancellationToken cancellationToken)
    {
        var hienTai = await _productService.GetByIdAsync(id, cancellationToken);

        if (hienTai is null)
        {
            // DbUpdateConcurrencyException cũng nổ khi bản ghi đã bị XOÁ (WHERE
            // khớp 0 dòng vì không còn dòng nào), không chỉ khi RowVersion lệch.
            ModelState.AddModelError(string.Empty,
                "Sản phẩm này đã bị người khác xoá. Không thể lưu thay đổi.");

            model.RowVersion = null;
        }
        else
        {
            ModelState.AddModelError(string.Empty,
                $"Người khác đã sửa sản phẩm này trong lúc bạn đang mở form. " +
                $"Giá trị hiện tại trong hệ thống: tên '{hienTai.Name}', " +
                $"giá {hienTai.Price.ToMoneyText()} đ, tồn kho {hienTai.Stock}. " +
                $"Kiểm tra lại rồi bấm Lưu để ghi đè, hoặc bấm Huỷ để giữ bản của họ.");

            // Nạp phiên bản mới, nếu không thì lần Lưu tiếp theo lại xung đột y
            // như cũ và người dùng mắc kẹt trong vòng lặp không có cách nào ra.
            //
            // Ghi vào model được vì hidden field render value THỦ CÔNG. Nếu dùng
            // asp-for thì giá trị POST lên trong ModelState sẽ THẮNG giá trị trong
            // model, và dòng này im lặng không có tác dụng.
            model.RowVersion = hienTai.RowVersion;
        }

        model.Categories = await LayDanhSachDanhMucAsync(cancellationToken);

        return View(model);
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
