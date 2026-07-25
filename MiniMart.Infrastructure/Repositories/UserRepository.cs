using Microsoft.EntityFrameworkCore;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly MiniMartDbContext _context;

    public UserRepository(MiniMartDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        // Collation mặc định của SQL Server không phân biệt hoa/thường, nên
        // "Tuan" và "tuan" được coi là một - khớp với hành vi của unique index.
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    public async Task<bool> ExistsByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        // AnyAsync sinh ra EXISTS, không kéo cả row về như FirstOrDefault.
        return await _context.Users
            .AnyAsync(u => u.Username == username, cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        // Chỉ đánh dấu Added trong Change Tracker, chưa chạm tới DB.
        await _context.Users.AddAsync(user, cancellationToken);
    }

}
