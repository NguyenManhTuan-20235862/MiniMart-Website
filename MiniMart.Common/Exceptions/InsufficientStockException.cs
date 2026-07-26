namespace MiniMart.Common.Exceptions;

/// <summary>
/// Tồn kho không đủ cho số lượng đang muốn đặt.
///
/// <para>
/// Tách khỏi <see cref="OutOfStockException"/> (hết sạch, dùng khi thêm vào giỏ) vì
/// hai tình huống dẫn tới hai hành động khác nhau của người dùng: hết sạch thì bỏ
/// món đó, còn thiếu thì giảm số lượng xuống. Thông báo phải nói rõ còn bao nhiêu.
/// </para>
/// <para>
/// Được ném ở CẢ HAI nguyên nhân, cố ý gộp: (1) đọc lên đã thấy không đủ, và
/// (2) đủ lúc đọc nhưng người khác mua trước nên <c>RowVersion</c> lệch lúc ghi.
/// Với người dùng cả hai là một chuyện - "vừa có người mua mất" - và cả hai đều
/// dẫn tới cùng một việc cần làm: cập nhật giỏ hàng.
/// </para>
/// </summary>
public class InsufficientStockException : Exception
{
    public InsufficientStockException(string productName, int available, int requested)
        : base(available <= 0
            ? $"Sản phẩm '{productName}' vừa hết hàng, vui lòng cập nhật giỏ hàng."
            : $"Sản phẩm '{productName}' chỉ còn {available} (bạn đặt {requested}), " +
              "vui lòng cập nhật giỏ hàng.")
    {
        ProductName = productName;
        Available = available;
        Requested = requested;
    }

    /// <summary>Dùng khi xung đột RowVersion: không biết chắc còn bao nhiêu vì con số vừa đổi.</summary>
    public InsufficientStockException(string productName, Exception? innerException)
        : base($"Sản phẩm '{productName}' vừa hết hàng, vui lòng cập nhật giỏ hàng.",
            innerException)
    {
        ProductName = productName;
        Available = 0;
        Requested = 0;
    }

    public string ProductName { get; }

    public int Available { get; }

    public int Requested { get; }
}
