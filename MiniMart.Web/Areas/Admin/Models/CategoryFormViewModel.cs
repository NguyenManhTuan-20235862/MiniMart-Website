using System.ComponentModel.DataAnnotations;

namespace MiniMart.Web.Areas.Admin.Models;

public class CategoryFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên danh mục.")]
    [StringLength(100, ErrorMessage = "Tên danh mục tối đa 100 ký tự.")]
    [Display(Name = "Tên danh mục")]
    public string Name { get; set; } = string.Empty;
}
