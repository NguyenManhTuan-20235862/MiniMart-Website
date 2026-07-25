namespace MiniMart.Common.Exceptions;

/// <summary>
/// Exception nghiệp vụ, không phụ thuộc công nghệ lưu trữ nào. Infrastructure
/// dịch DbUpdateException của EF Core sang exception này, nhờ đó tầng
/// Application và Web không cần biết EF Core tồn tại.
/// </summary>
public class UsernameAlreadyExistsException : Exception
{
    public string Username { get; }

    public UsernameAlreadyExistsException(string username)
        : base($"Tên đăng nhập '{username}' đã được sử dụng.")
    {
        Username = username;
    }
}
