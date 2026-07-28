namespace MiniMart.Application.Models;

/// <summary>
/// Một dòng bị BỎ QUA vì người khác đã sửa nó sau khi bảng được mở.
///
/// <para>
/// Mang theo giá trị HIỆN TẠI trong DB, không phải giá trị người dùng gửi lên: câu hỏi
/// của Admin lúc này là "người kia đã đổi thành gì" chứ không phải "tôi vừa gõ gì" -
/// thứ họ vẫn đang nhìn thấy trên màn hình.
/// </para>
/// <para>
/// <paramref name="RowVersionHienTai"/> null nghĩa là sản phẩm đã bị XOÁ hẳn, không
/// phải bị sửa. Hai chuyện khác nhau với người đọc thông báo.
/// </para>
/// </summary>
public record ProductConflict(
    int ProductId,
    string ProductName,
    decimal PriceHienTai,
    int StockHienTai,
    byte[]? RowVersionHienTai)
{
    public bool DaBiXoa => RowVersionHienTai is null;
}

/// <summary>
/// Kết quả một lần sửa hàng loạt: phần đã lưu, và phần bị bỏ qua.
///
/// <para>
/// Trả về một object thay vì <c>int</c> vì thao tác này có hai kết cục ĐỒNG THỜI xảy ra
/// được - lưu được 18 dòng VÀ bỏ qua 2 dòng. Nhét vào một con số thì tầng trên không có
/// cách nào nói cho người dùng biết dòng nào bị bỏ, mà đó chính là yêu cầu.
/// </para>
/// </summary>
public record BulkUpdateResult(int SoDongDaLuu, IReadOnlyList<ProductConflict> XungDot)
{
    public bool CoXungDot => XungDot.Count > 0;
}
