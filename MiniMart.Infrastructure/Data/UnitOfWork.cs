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
        catch (DbUpdateConcurrencyException ex)
        {
            // Dịch sang exception CHUNG vì Application và Web không được
            // using Microsoft.EntityFrameworkCore - không dịch thì hai tầng đó
            // không có cách nào bắt được xung đột mà vẫn giữ quy ước phân tầng.
            //
            // DbUpdateConcurrencyException chỉ xảy ra khi UPDATE/DELETE có
            // concurrency token và số dòng bị ảnh hưởng là 0, tức WHERE không
            // khớp: hoặc RowVersion đã đổi, hoặc bản ghi đã bị xoá. Cả hai đều
            // là "người khác đã thay đổi", nên gộp về một exception là đúng nghĩa.
            var entry = ex.Entries.FirstOrDefault();
            var entityName = entry?.Metadata.ClrType.Name ?? "Bản ghi";
            var id = entry?.Properties
                .FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue;

            throw new ConcurrencyConflictException(entityName, id, ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Nơi duy nhất trong hệ thống được biết EF Core và mã lỗi SQL Server.
            // Tầng trên chỉ thấy DuplicateKeyException.
            throw new DuplicateKeyException(ex);
        }
    }

    // 2601 = trùng khoá trên unique index, 2627 = vi phạm unique constraint.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx && sqlEx.Number is 2601 or 2627;
}
