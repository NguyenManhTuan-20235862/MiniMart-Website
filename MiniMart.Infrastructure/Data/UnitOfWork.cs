using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Infrastructure.Data;

public class UnitOfWork : IUnitOfWork
{
    private readonly MiniMartDbContext _context;

    public UnitOfWork(MiniMartDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Nơi duy nhất trong hệ thống được biết EF Core và mã lỗi SQL Server.
            // Tầng trên chỉ thấy DuplicateKeyException.
            throw new DuplicateKeyException(ex);
        }

        // DbUpdateConcurrencyException CỐ Ý không bắt ở đây: nó đã là khái niệm
        // đủ rõ nghĩa, và mỗi nghiệp vụ xử lý xung đột theo cách riêng.
    }

    // 2601 = trùng khoá trên unique index, 2627 = vi phạm unique constraint.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx && sqlEx.Number is 2601 or 2627;
}
