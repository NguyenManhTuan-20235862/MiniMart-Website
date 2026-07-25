namespace MiniMart.Common.Exceptions;

/// <summary>
/// Vi phạm ràng buộc duy nhất ở tầng DB, đã được dịch sang khái niệm nghiệp vụ.
///
/// Cố ý giữ ở mức CHUNG: Infrastructure chỉ biết "có unique constraint bị vi
/// phạm", còn việc quy nó thành lỗi nghiệp vụ nào (username trùng, tên danh mục
/// trùng...) là việc của tầng Application - nơi biết mình vừa định làm gì.
/// </summary>
public class DuplicateKeyException : Exception
{
    public DuplicateKeyException(Exception innerException)
        : base("Dữ liệu vi phạm ràng buộc duy nhất.", innerException)
    {
    }
}
