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
    /// Ném <see cref="Common.Exceptions.DuplicateKeyException"/> khi vi phạm ràng
    /// buộc duy nhất, và <see cref="Common.Exceptions.ConcurrencyConflictException"/>
    /// khi xung đột RowVersion (hoặc bản ghi đã bị xoá).
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Mở transaction bao nhiều thao tác ghi phải CÙNG thành công hoặc CÙNG bị bỏ.
    ///
    /// <para>
    /// API này được hoãn từ đầu dự án và chỉ thêm vào khi có nghiệp vụ thật cần nó -
    /// đặt hàng. Lý do hoãn: không biết trước transaction phải bao nhiêu thao tác,
    /// có cần lồng nhau không, isolation level nào. Giờ đã có câu trả lời cụ thể:
    /// bao "trừ tồn kho nhiều sản phẩm + tạo Order + tạo OrderDetail + xoá giỏ",
    /// KHÔNG cần lồng nhau, và isolation mặc định (ReadCommitted) là đủ vì việc
    /// chống lost update do RowVersion đảm nhiệm chứ không phải do isolation level.
    /// </para>
    /// <para>
    /// Vẫn KHÔNG thêm tham số <c>IsolationLevel</c>: chưa có nghiệp vụ nào cần mức
    /// khác. Thêm khi cần, không thêm trước.
    /// </para>
    /// </summary>
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
