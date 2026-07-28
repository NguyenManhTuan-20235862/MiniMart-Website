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

    Task<List<User>> GetUsersAsync(int page = 1, int pageSize = 20, string? search = null, CancellationToken cancellationToken = default);

    Task<int> CountUsersAsync(string? search = null, CancellationToken cancellationToken = default);

    // Lưu thay đổi nằm ở IUnitOfWork - xem ghi chú trong interface đó.
}
