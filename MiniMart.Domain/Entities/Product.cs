namespace MiniMart.Domain.Entities;

public class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>decimal chứ KHÔNG phải double: tiền tệ cần chính xác tuyệt đối.</summary>
    public decimal Price { get; set; }

    public int Stock { get; set; }

    /// <summary>
    /// Đường dẫn tương đối tới ảnh, ví dụ "/images/products/abc.jpg".
    /// DB chỉ lưu đường dẫn; file thật nằm trong wwwroot. Null = chưa có ảnh.
    /// </summary>
    public string? ImageUrl { get; set; }

    // Khoá ngoại tường minh. Có sẵn property này thì gán CategoryId trực tiếp
    // được mà không cần load cả object Category từ DB.
    public int CategoryId { get; set; }

    /// <summary>Navigation property - đầu "1" của quan hệ.</summary>
    public Category Category { get; set; } = null!;

    /// <summary>
    /// Concurrency token. SQL Server tự tăng giá trị này mỗi lần row bị UPDATE;
    /// EF Core đưa nó vào mệnh đề WHERE để phát hiện xung đột ghi đồng thời.
    /// KHÔNG tự gán giá trị cho nó trong code.
    /// </summary>
    public byte[] RowVersion { get; set; } = null!;
}
