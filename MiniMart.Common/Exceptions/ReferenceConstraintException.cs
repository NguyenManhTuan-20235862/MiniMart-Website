namespace MiniMart.Common.Exceptions;

/// <summary>
/// Xoá một bản ghi đang được bản ghi khác tham chiếu (vi phạm khoá ngoại Restrict).
///
/// <para>
/// Exception CHUNG do Infrastructure dịch từ mã lỗi SQL Server 547 - cùng vai trò
/// với <see cref="DuplicateKeyException"/>. Application diễn giải nó thành exception
/// NGHIỆP VỤ theo ngữ cảnh (<c>CategoryHasProductsException</c>,
/// <c>ProductHasOrdersException</c>), vì cùng một mã 547 có nghĩa khác nhau tuỳ chỗ.
/// </para>
/// <para>
/// Tồn tại để bịt khe TOCTOU: Service kiểm tra trước khi xoá, nhưng giữa lúc kiểm và
/// lúc xoá vẫn có thể có người vừa đặt hàng. Không có exception này thì trường hợp
/// hiếm đó cho ra HTTP 500 kèm thông báo của EF Core.
/// </para>
/// </summary>
public class ReferenceConstraintException : Exception
{
    public ReferenceConstraintException(Exception? innerException)
        : base("Không thể xoá vì bản ghi này đang được dữ liệu khác tham chiếu.",
            innerException)
    {
    }
}
