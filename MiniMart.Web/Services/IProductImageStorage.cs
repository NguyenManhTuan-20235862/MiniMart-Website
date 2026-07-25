namespace MiniMart.Web.Services;

/// <summary>
/// Lưu trữ ảnh sản phẩm. Đặt ở tầng Web vì wwwroot là khái niệm của web host —
/// tầng Application chỉ nhận về một chuỗi đường dẫn và không cần biết file
/// nằm ở đâu.
/// </summary>
public interface IProductImageStorage
{
    /// <summary>Trả về đường dẫn tương đối để lưu vào Product.ImageUrl.</summary>
    Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default);

    /// <summary>Xoá file cũ. Bỏ qua im lặng nếu file không còn tồn tại.</summary>
    void Delete(string? imageUrl);
}
