using MiniMart.Common;
using MiniMart.Domain.Entities;

namespace MiniMart.Web.Models;

public class HomeIndexViewModel
{
    public required PagedResult<Product> Products { get; init; }

    /// <summary>Danh sách danh mục để dựng bộ lọc.</summary>
    public required IReadOnlyList<Category> Categories { get; init; }

    /// <summary>Danh mục đang được chọn; null = xem tất cả. Dùng để giữ trạng thái form.</summary>
    public int? SelectedCategoryId { get; init; }
}
