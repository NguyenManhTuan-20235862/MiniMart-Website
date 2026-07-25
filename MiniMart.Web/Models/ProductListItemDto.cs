namespace MiniMart.Web.Models;

/// <summary>
/// Hợp đồng JSON gửi ra client. CỐ Ý tách khỏi entity Product:
///
/// 1. Product.Category trỏ ngược về Category.Products -> vòng lặp tham chiếu,
///    System.Text.Json sẽ ném exception.
/// 2. Không lộ RowVersion (vô nghĩa với client) và Stock (thông tin kinh doanh).
/// 3. Đổi tên property trong entity không âm thầm làm hỏng client đang dùng.
///
/// System.Text.Json mặc định đổi sang camelCase: id, name, price, imageUrl.
/// </summary>
public record ProductListItemDto(int Id, string Name, decimal Price, string? ImageUrl);

/// <summary>
/// Bọc thêm thông tin phân trang. Thiếu HasNextPage thì client cuộn vô tận,
/// gọi mãi không biết dừng ở đâu.
/// </summary>
public record LoadMoreResponse(
    IReadOnlyList<ProductListItemDto> Items,
    int Page,
    bool HasNextPage);
