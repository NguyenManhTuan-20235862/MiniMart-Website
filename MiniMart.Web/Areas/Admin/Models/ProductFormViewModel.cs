using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

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

    [Range(0, 999_999_999, ErrorMessage = "Giá phải từ 0 trở lên.")]
    [Display(Name = "Giá (VNĐ)")]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Tồn kho không được âm.")]
    [Display(Name = "Tồn kho")]
    public int Stock { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục.")]
    [Display(Name = "Danh mục")]
    public int CategoryId { get; set; }

    /// <summary>Chỉ phục vụ hiển thị dropdown, không phải dữ liệu người dùng gửi lên.</summary>
    public IEnumerable<SelectListItem> Categories { get; set; } = [];
}
