using Microsoft.EntityFrameworkCore.Storage;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Infrastructure.Data;

/// <summary>
/// Bọc <see cref="IDbContextTransaction"/> của EF Core lại thành
/// <see cref="ITransaction"/> để tầng trên không phải biết EF Core.
///
/// <para>
/// Mỏng có chủ đích: nó không thêm logic nào, chỉ đổi kiểu. Mọi hành vi thật
/// (rollback khi dispose mà chưa commit) là của EF Core.
/// </para>
/// </summary>
internal sealed class EfTransaction : ITransaction
{
    private readonly IDbContextTransaction _transaction;

    public EfTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        _transaction.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        _transaction.RollbackAsync(cancellationToken);

    public ValueTask DisposeAsync() => _transaction.DisposeAsync();
}
