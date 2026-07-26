namespace MiniMart.Domain.Interfaces;

/// <summary>
/// Một transaction đang mở. Trừu tượng hoá vì Domain và Application không được
/// biết <c>IDbContextTransaction</c> của EF Core tồn tại.
///
/// <para>
/// Cách dùng bắt buộc là <c>await using</c> + gọi <see cref="CommitAsync"/> tường
/// minh ở cuối đường thành công:
/// </para>
/// <code>
/// await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
/// ... nhiều thao tác ghi ...
/// await _unitOfWork.SaveChangesAsync(ct);
/// await tx.CommitAsync(ct);
/// </code>
/// <para>
/// Không commit thì <c>DisposeAsync</c> ROLLBACK. Đó là mặc định an toàn: thoát
/// giữa đường vì exception thì không có gì được ghi, và không cần <c>try/catch</c>
/// chỉ để rollback.
/// </para>
/// </summary>
public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback tường minh. Không bắt buộc gọi (Dispose đã rollback nếu chưa
    /// commit), nhưng gọi trong <c>catch</c> làm ý định hiện rõ ở chỗ đọc code.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
