namespace MiniMart.Domain.Entities;

public class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>
    /// Đầu "N" của quan hệ 1-N. Khởi tạo sẵn danh sách rỗng để tránh
    /// NullReferenceException khi tạo Category mới bằng tay.
    /// </summary>
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
