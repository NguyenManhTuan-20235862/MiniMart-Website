namespace MiniMart.Common.Exceptions;

/// <summary>
/// Vi phạm quy tắc nghiệp vụ: không được xoá danh mục còn sản phẩm.
/// Khác với DuplicateKeyException ở chỗ đây là quy tắc do NGHIỆP VỤ đặt ra,
/// không phải lỗi hạ tầng được dịch lại.
/// </summary>
public class CategoryHasProductsException : Exception
{
    public int CategoryId { get; }

    public CategoryHasProductsException(int categoryId)
        : base("Không thể xoá danh mục đang còn sản phẩm. Hãy chuyển hoặc xoá sản phẩm trước.")
    {
        CategoryId = categoryId;
    }
}
