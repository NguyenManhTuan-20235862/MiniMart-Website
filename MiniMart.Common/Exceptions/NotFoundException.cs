namespace MiniMart.Common.Exceptions;

public class NotFoundException : Exception
{
    /// <summary>
    /// Tên entity không tìm thấy. Cần thiết để tầng Web phân biệt được: thiếu
    /// chính đối tượng đang sửa (→ 404) hay thiếu đối tượng được tham chiếu
    /// (→ báo lỗi trên đúng ô nhập và render lại form).
    /// </summary>
    public string EntityName { get; }

    public object Key { get; }

    public NotFoundException(string entityName, object key)
        : base($"Không tìm thấy {entityName} với mã '{key}'.")
    {
        EntityName = entityName;
        Key = key;
    }
}
