namespace MiniMart.Domain.Interfaces;

/// <summary>
/// Ranh giới lưu dữ liệu. Tách khỏi Repository vì phạm vi của nó khác hẳn:
/// Repository thao tác trên MỘT loại entity, còn SaveChanges commit TẤT CẢ
/// thay đổi đang chờ trong cùng một request.
///
/// Trước khi tách, mỗi repository đều có SaveChangesAsync riêng nhưng thực chất
/// gọi chung một DbContext - tên gọi nói dối về phạm vi ảnh hưởng.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Ném <see cref="Common.Exceptions.DuplicateKeyException"/> khi vi phạm
    /// ràng buộc duy nhất, và DbUpdateConcurrencyException khi xung đột RowVersion.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
