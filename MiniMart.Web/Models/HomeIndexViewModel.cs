using MiniMart.Common;
using MiniMart.Domain.Entities;

namespace MiniMart.Web.Models;

public class HomeIndexViewModel
{
    public required PagedResult<Product> Products { get; init; }

    /// <summary>Danh sách danh mục để dựng bộ lọc.</summary>
    public required IReadOnlyList<Category> Categories { get; init; }

    /// <summary>
    /// Bộ lọc đang áp dụng. Cùng type mà ProductController.LoadMore nhận, nên
    /// view lấy được đủ giá trị để dựng lại URL trang sau qua data-*.
    /// </summary>
    public required ProductFilter Filter { get; init; }
}
