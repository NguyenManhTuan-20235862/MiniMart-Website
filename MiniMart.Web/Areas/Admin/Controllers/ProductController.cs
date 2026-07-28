using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MiniMart.Application.Interfaces;
using MiniMart.Application.Models;
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
    /// <summary>
    /// Số dòng mỗi trang của bảng sửa hàng loạt.
    ///
    /// <para>
    /// 20 chứ không phải 12 như trang khách: người sửa hàng loạt muốn thấy nhiều dòng
    /// một lúc. Và phải ở rất xa trần 1024 của model binder - lấy 100 (trần của
    /// repository) là để một lần đổi cấu hình nữa sẽ chạm giới hạn.
    /// </para>
    /// </summary>
    private const int KichThuocTrangSuaHangLoat = 20;

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

    /// <summary>
    /// Bảng sửa giá / tồn kho hàng loạt.
    ///
    /// <para>
    /// Dùng LẠI nguyên <c>IProductService.GetProductsAsync</c> của Phase 3 thay vì
    /// viết một truy vấn mới cho Admin. Lợi ích không chỉ là đỡ code: mọi thứ đã khoá
    /// ở đó - <c>OrderBy</c> có tie-breaker <c>ThenBy(Id)</c>, kẹp <c>page</c>/
    /// <c>pageSize</c>, đếm tổng theo bộ lọc - tự động đúng ở đây. Viết truy vấn thứ
    /// hai là tạo cơ hội cho một trong số đó bị quên, mà thiếu tie-breaker thì bản ghi
    /// nhảy giữa hai trang và người dùng sửa nhầm dòng.
    /// </para>
    /// <para>
    /// <b>Bắt buộc phân trang</b>, không hiện "tất cả": model binder có trần
    /// <c>MaxModelBindingCollectionSize</c> = 1024 phần tử, vượt là
    /// <c>InvalidOperationException</c> → HTTP 500 chứ không phải lỗi tử tế. Và một
    /// form 5000 dòng thì trình duyệt cũng không dùng được.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> BulkEdit(int page = 1, CancellationToken cancellationToken = default)
    {
        var model = await DungBangAsync(page, cancellationToken);

        // ★ BẮT BUỘC, và đây là loại lỗi chỉ test mới bắt được.
        //
        // `asp-for` KHÔNG đọc thẳng từ Model: nó ưu tiên giá trị trong ModelState, vì
        // mục đích của nó là giữ lại đúng những gì người dùng vừa gõ khi form được
        // render lại sau lỗi validation.
        //
        // Ở đây điều đó phản tác dụng: tham số `page` đã được model binder đưa vào
        // ModelState với giá trị THÔ từ query string. Repository kẹp -5 về 1, Model
        // mang Page = 1, nhưng hidden field vẫn render value="-5" - và lần submit tới
        // gửi lại đúng giá trị bậy đó. Đã đo: test đọc ra "-5" thay vì "1".
        //
        // Quy tắc tổng quát: SERVER SỬA một giá trị thì phải xoá khoá đó khỏi
        // ModelState, nếu không giao diện vẫn hiện giá trị cũ của người dùng.
        ModelState.Remove(nameof(page));

        return View(model);
    }

    /// <summary>
    /// Lưu cả bảng.
    ///
    /// <para>
    /// <b>Thất bại thì RENDER LẠI, không redirect</b> — cùng lý do với form đặt hàng:
    /// redirect vứt sạch giá và tồn kho người dùng vừa gõ cho 20 dòng. PRG chỉ giữ cho
    /// nhánh THÀNH CÔNG, nơi F5 mới có hại.
    /// </para>
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkEdit(
        ProductBulkUpdateViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            // KHÔNG nạp RowVersion mới ở nhánh này: form hỏng vì người dùng gõ sai,
            // không phải vì có xung đột. Thay phiên bản lúc này là âm thầm gia hạn
            // "giấy phép ghi đè" mà họ không hề biết.
            return View(await LamMoiTheoDongDaGuiAsync(model, napRowVersionMoi: false, cancellationToken));
        }

        BulkUpdateResult ketQua;

        try
        {
            ketQua = await _productService.BulkUpdatePriceStockAsync(
                model.Items
                    .Select(i => new ProductBulkUpdateItem(i.Id, i.Price, i.Stock, i.RowVersion))
                    .ToList(),
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Chỉ còn cửa sổ HẸP mới tới được đây (ai đó ghi đúng vào khoảng vài mili
            // giây giữa lệnh đọc và lệnh ghi của Service). Cả batch đã bị bỏ.
            return await RenderLaiBangAsync(model,
                "Có sản phẩm vừa bị thay đổi đúng lúc hệ thống đang lưu, nên "
                + "KHÔNG có thay đổi nào được ghi. Dữ liệu bạn nhập vẫn còn bên dưới - "
                + "bấm Lưu lần nữa.",
                xungDot: [],
                cancellationToken);
        }

        if (ketQua.CoXungDot)
        {
            return await RenderLaiBangAsync(
                model, SoanThongBaoXungDot(ketQua), ketQua.XungDot, cancellationToken);
        }

        // 0 là kết cục hợp lệ, không phải lỗi: người dùng mở bảng rồi bấm Lưu mà không
        // sửa gì. Nói "đã cập nhật 0 sản phẩm" thì đúng số nhưng đọc như một sự cố.
        TempData["Success"] = ketQua.SoDongDaLuu == 0
            ? "Không có thay đổi nào để lưu."
            : $"Đã cập nhật {ketQua.SoDongDaLuu} sản phẩm.";

        // Quay lại ĐÚNG trang vừa sửa, không phải trang 1 - người sửa hàng loạt thường
        // đi tiếp từ chỗ đang đứng.
        return RedirectToAction(nameof(BulkEdit), new { page = model.Page });
    }

    /// <summary>
    /// Soạn thông báo cho lần lưu có dòng bị bỏ qua.
    ///
    /// <para>
    /// Phải nói đủ BA điều, thiếu một là Admin phải tự đi dò: (1) bao nhiêu dòng ĐÃ lưu
    /// — nếu không họ tưởng cả lần bấm Lưu vừa rồi vô ích; (2) sản phẩm nào bị bỏ qua
    /// và người kia đã đổi thành GIÁ TRỊ GÌ; (3) làm gì tiếp.
    /// </para>
    /// </summary>
    private static string SoanThongBaoXungDot(BulkUpdateResult ketQua)
    {
        var daLuu = ketQua.SoDongDaLuu == 0
            ? "Không có dòng nào được lưu."
            : $"Đã lưu {ketQua.SoDongDaLuu} sản phẩm.";

        // CỐ Ý không liệt kê từng sản phẩm ở đây nữa: từ khi bảng tự đánh dấu từng dòng,
        // liệt kê lại là dựng một danh sách thứ hai mà người đọc phải đối chiếu bằng mắt
        // với danh sách thứ nhất. Câu này giữ đúng phần mà bảng KHÔNG nói được - tổng
        // kết và hướng xử lý.
        return $"{daLuu} {ketQua.XungDot.Count} sản phẩm bị BỎ QUA vì người khác vừa sửa "
            + "- các dòng đó được tô vàng bên dưới, kèm giá trị hiện tại trong hệ thống. "
            + "Giá trị bạn nhập vẫn còn nguyên; kiểm tra lại rồi bấm Lưu để ghi đè.";
    }

    /// <summary>
    /// Render lại bảng kèm một thông báo: GIỮ NGUYÊN mọi giá trị người dùng đã gõ, và
    /// nạp <c>RowVersion</c> MỚI cho <b>toàn bảng</b>.
    ///
    /// <para>
    /// "Toàn bảng" chứ không chỉ các dòng vướng, và đây là điểm dễ sai nhất của việc
    /// cho phép thành công một phần: dòng vừa lưu XONG cũng đã có phiên bản mới. Giữ
    /// phiên bản cũ cho chúng thì lần bấm Lưu tiếp theo sẽ báo xung đột ở đúng những
    /// dòng mà chính người dùng vừa ghi thành công.
    /// </para>
    /// <para>
    /// Thiếu bước nạp phiên bản mới thì người dùng bấm Lưu bao nhiêu lần cũng nhận đúng
    /// một lỗi và không có đường nào thoát ra — đúng điều
    /// <c>Sau_xung_dot_bam_Luu_lan_hai_thi_thanh_cong</c> khoá lại ở form sửa lẻ.
    /// </para>
    /// </summary>
    private async Task<IActionResult> RenderLaiBangAsync(
        ProductBulkUpdateViewModel model,
        string thongBao,
        IReadOnlyList<ProductConflict> xungDot,
        CancellationToken cancellationToken)
    {
        var moiNhat = await LamMoiTheoDongDaGuiAsync(model, napRowVersionMoi: true, cancellationToken);

        // Gắn xung đột vào ĐÚNG dòng để View đánh dấu ngay tại chỗ. Chỉ có câu thông
        // báo chung ở đầu trang là bắt Admin đối chiếu bằng mắt giữa một đoạn văn và 20
        // dòng bảng - đúng loại việc mà máy làm được còn người thì làm sai.
        var theoId = xungDot.ToDictionary(c => c.ProductId);

        foreach (var dong in moiNhat.Items)
        {
            dong.XungDot = theoId.GetValueOrDefault(dong.Id);
        }

        ModelState.AddModelError(string.Empty, thongBao);

        return View(moiNhat);
    }

    /// <summary>
    /// Dựng lại model để render lại bảng, giữ ĐÚNG thứ tự và số dòng của lần POST.
    ///
    /// <para>
    /// Cố ý KHÔNG gọi <see cref="DungBangAsync"/>: hàm đó đọc lại một TRANG từ DB, mà
    /// tập sản phẩm có thể đã đổi (thêm/xoá/đổi tên làm thứ tự <c>OrderBy(Name)</c>
    /// dịch chuyển). Chỉ số của <c>Items</c> lại đang neo giá trị người dùng gõ trong
    /// ModelState, nên lệch một dòng là giá của sản phẩm này nhảy sang sản phẩm khác.
    /// Dựng theo danh sách ĐÃ GỬI thì chỉ số luôn khớp.
    /// </para>
    /// <para>
    /// Hai truy vấn ở đường lỗi: một để lấy tên + phiên bản mới, một để lấy tổng số
    /// trang cho phần phân trang. Chấp nhận được vì đây không phải đường đi thường.
    /// </para>
    /// </summary>
    private async Task<ProductBulkUpdateViewModel> LamMoiTheoDongDaGuiAsync(
        ProductBulkUpdateViewModel model,
        bool napRowVersionMoi,
        CancellationToken cancellationToken)
    {
        // Name có [BindNever] nên sau model binding nó RỖNG. Không nạp lại thì bảng
        // hiện ra với cột tên trắng trơn - và không exception nào, vì chuỗi rỗng hợp lệ.
        var moiNhat = (await _productService.GetByIdsAsync(
                model.Items.Select(i => i.Id), cancellationToken))
            .ToDictionary(p => p.Id);

        foreach (var dong in model.Items)
        {
            if (moiNhat.TryGetValue(dong.Id, out var product))
            {
                dong.Name = product.Name;

                if (napRowVersionMoi)
                {
                    // Ghi vào model được vì hidden field RowVersion render value THỦ
                    // CÔNG. Dùng asp-for thì giá trị POST lên trong ModelState sẽ
                    // THẮNG, và dòng này im lặng không có tác dụng.
                    dong.RowVersion = product.RowVersion;
                }
            }
            else
            {
                dong.Name = "(sản phẩm đã bị xoá)";
                dong.RowVersion = null;
            }
        }

        var trang = await _productService.GetProductsAsync(
            page: model.Page, pageSize: KichThuocTrangSuaHangLoat,
            cancellationToken: cancellationToken);

        model.TongSoTrang = trang.TotalPages;
        model.TongSoSanPham = trang.TotalCount;

        return model;
    }

    /// <summary>
    /// Đọc một trang sản phẩm và đổ vào model của bảng sửa hàng loạt.
    ///
    /// <para>
    /// Tách riêng vì đường POST (bước sau) sẽ phải gọi lại đúng hàm này để render lại
    /// bảng khi validation hỏng: <c>Name</c> và <c>TongSoTrang</c> đều có
    /// <c>[BindNever]</c> nên sau model binding chúng RỖNG. Quên nạp lại là bảng hiện
    /// ra với cột tên trống trơn - không exception nào, vì chuỗi rỗng hợp lệ.
    /// </para>
    /// </summary>
    private async Task<ProductBulkUpdateViewModel> DungBangAsync(
        int page,
        CancellationToken cancellationToken)
    {
        var trang = await _productService.GetProductsAsync(
            page: page, pageSize: KichThuocTrangSuaHangLoat, cancellationToken: cancellationToken);

        return new ProductBulkUpdateViewModel
        {
            // Thứ tự các dòng ở đây CHÍNH LÀ thứ tự chỉ số Items[0..n-1] mà View sẽ
            // render. Nó phải ổn định giữa lần GET và lần POST render lại, nếu không
            // người dùng thấy giá vừa nhập nhảy sang dòng khác.
            Items = trang.Items
                .Select(p => new ProductBulkUpdateDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price,
                    Stock = p.Stock,

                    // Chụp phiên bản NGAY LÚC NÀY - mỗi dòng một mốc riêng. Đây là thứ
                    // lần lưu tới sẽ so với, và là toàn bộ lý do bảng này an toàn khi
                    // hai admin cùng mở.
                    RowVersion = p.RowVersion
                })
                .ToList(),

            // Page lấy từ KẾT QUẢ chứ không từ tham số: repository đã kẹp giá trị bậy
            // (?page=-5 thành 1), nên đọc lại từ đó mới ra số trang thật sự đang xem.
            Page = trang.Page,
            TongSoTrang = trang.TotalPages,
            TongSoSanPham = trang.TotalCount
        };
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
