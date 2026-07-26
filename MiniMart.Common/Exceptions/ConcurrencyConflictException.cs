namespace MiniMart.Common.Exceptions;

/// <summary>
/// Bản ghi đã bị người khác sửa từ lúc người dùng hiện tại đọc nó.
///
/// <para>
/// Đây là bản dịch của <c>DbUpdateConcurrencyException</c> sang khái niệm chung.
/// Trước đây dự án CỐ Ý không bọc exception đó lại, nhưng quy ước này phải đổi
/// khi tầng Web bắt đầu xử lý xung đột: Web và Application không được
/// <c>using Microsoft.EntityFrameworkCore</c>, nên không có cách nào bắt được
/// một exception của EF Core mà vẫn giữ được quy ước phân tầng.
/// </para>
/// <para>
/// Mang theo <see cref="EntityName"/> và <see cref="Id"/> để tầng trên soạn
/// thông báo có ngữ cảnh, giống <see cref="NotFoundException"/>.
/// </para>
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public string EntityName { get; }

    public object? Id { get; }

    public ConcurrencyConflictException(string entityName, object? id, Exception? innerException = null)
        : base($"{entityName} (Id = {id}) đã bị người khác thay đổi sau khi bạn mở form.",
               innerException)
    {
        EntityName = entityName;
        Id = id;
    }
}
