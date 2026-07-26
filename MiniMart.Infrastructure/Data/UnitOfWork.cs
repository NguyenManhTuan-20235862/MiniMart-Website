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

    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        // Cùng DbContext nên mọi SaveChangesAsync sau đây tự chạy trong transaction
        // này - không phải truyền transaction đi đâu cả. Đó là lý do API transaction
        // thuộc về IUnitOfWork chứ không phải từng Repository.
        var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        return new EfTransaction(transaction);
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
        catch (DbUpdateException ex) when (LaLoiSql(ex, 2601, 2627))
        {
            // Nơi duy nhất trong hệ thống được biết EF Core và mã lỗi SQL Server.
            // Tầng trên chỉ thấy DuplicateKeyException.
            throw new DuplicateKeyException(ex);
        }
        catch (DbUpdateException ex) when (LaLoiSql(ex, 547))
        {
            // 547 = vi phạm ràng buộc khoá ngoại HOẶC check constraint.
            //
            // Ở dự án này nó gần như luôn là khoá ngoại Restrict (xoá Category còn
            // Product, xoá Product đã có OrderDetail) vì mọi check constraint đều đã
            // được Service kiểm trước. Không tách riêng hai nguyên nhân: thông điệp
            // cho tầng trên là "thao tác này bị dữ liệu khác chặn", và Application
            // mới là nơi biết dữ liệu khác đó là gì.
            throw new ReferenceConstraintException(ex);
        }
    }

    /// <summary>
    /// 2601 = trùng khoá trên unique index, 2627 = vi phạm unique constraint,
    /// 547 = vi phạm khoá ngoại / check constraint.
    /// </summary>
    private static bool LaLoiSql(DbUpdateException ex, params int[] maLoi) =>
        ex.InnerException is SqlException sqlEx && maLoi.Contains(sqlEx.Number);
}
