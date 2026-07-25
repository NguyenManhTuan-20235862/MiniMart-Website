using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using MiniMart.Web.Validation;

namespace MiniMart.Web.Areas.Admin.Models;

/// <summary>
/// ViewModel riêng thay vì bind thẳng vào entity Product. Lý do quan trọng
/// nhất là chống over-posting: bind thẳng entity thì kẻ tấn công có thể POST
/// thêm trường RowVersion hoặc Id và model binder sẽ nhận hết.
/// </summary>
public class ProductFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
    [StringLength(200, ErrorMessage = "Tên sản phẩm tối đa 200 ký tự.")]
    [Display(Name = "Tên sản phẩm")]
    public string Name { get; set; } = string.Empty;

    // Dùng overload typeof(decimal) + chuỗi thay vì Range(0.01, ...) vì overload
    // kia nhận double, làm tròn nhị phân trước khi so sánh với decimal.
    // ConvertValueInInvariantCulture: không có nó, máy dùng locale vi-VN sẽ hiểu
    // "0.01" theo dấu phân cách thập phân là dấu phẩy và parse ra số khác hẳn.
    [Range(typeof(decimal), "0.01", "999999999",
        ConvertValueInInvariantCulture = true,
        ErrorMessage = "Giá phải lớn hơn 0.")]
    [Display(Name = "Giá (VNĐ)")]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Tồn kho không được âm.")]
    [Display(Name = "Tồn kho")]
    public int Stock { get; set; }

    // Data Annotation chỉ kiểm được "đã chọn gì đó chưa" - đây là ràng buộc
    // thuộc về bản thân giá trị. Còn "danh mục đó có TỒN TẠI không" phụ thuộc
    // trạng thái DB nên nằm ở ProductService, xem BaoDamDanhMucTonTaiAsync.
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục.")]
    [Display(Name = "Danh mục")]
    public int CategoryId { get; set; }

    [ImageFile(MaxSizeInMb = 2)]
    [Display(Name = "Ảnh sản phẩm")]
    public IFormFile? ImageFile { get; set; }

    /// <summary>Ảnh đang có, để form Edit hiển thị và giữ lại khi không chọn ảnh mới.</summary>
    public string? ExistingImageUrl { get; set; }

    /// <summary>Chỉ phục vụ hiển thị dropdown, không phải dữ liệu người dùng gửi lên.</summary>
    public IEnumerable<SelectListItem> Categories { get; set; } = [];
}
