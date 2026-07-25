using MiniMart.Domain.Entities;

namespace MiniMart.Domain.Interfaces;

/// <summary>
/// Domain khai báo thứ nó CẦN từ tầng lưu trữ. Không biết EF Core, không biết
/// SQL Server - chỉ mô tả các thao tác nghiệp vụ cần đến dữ liệu.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tách riêng khỏi AddAsync để giữ Unit of Work: nhiều thao tác gom vào
    /// một lần lưu duy nhất, cùng thành công hoặc cùng thất bại.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
