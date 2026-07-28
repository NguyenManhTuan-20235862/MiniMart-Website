using MiniMart.Common;
using MiniMart.Domain.Entities;

namespace MiniMart.Application.Interfaces;

public interface IUserService
{
    /// <summary>Ném <see cref="Common.Exceptions.UsernameAlreadyExistsException"/> nếu username đã tồn tại.</summary>
    Task<User> RegisterAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trả về null khi xác thực thất bại - KHÔNG phân biệt sai username hay sai
    /// mật khẩu, tránh để lộ tài khoản nào tồn tại (user enumeration).
    /// </summary>
    Task<User?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task<PagedResult<User>> GetUsersAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CancellationToken cancellationToken = default);
}
