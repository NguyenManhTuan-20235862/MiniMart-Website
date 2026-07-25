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

    /// <summary>Giá tối thiểu đang lọc; null = không giới hạn dưới.</summary>
    public decimal? MinPrice { get; init; }

    /// <summary>Giá tối đa đang lọc; null = không giới hạn trên.</summary>
    public decimal? MaxPrice { get; init; }

    /// <summary>
    /// Có filter nào đang bật hay không. Dùng để quyết định hiện link "Xoá lọc" -
    /// hiện nút này khi không lọc gì thì vô nghĩa.
    /// </summary>
    public bool HasAnyFilter =>
        SelectedCategoryId is not null || MinPrice is not null || MaxPrice is not null;
}
