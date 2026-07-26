namespace MiniMart.Domain.ValueObjects;

/// <summary>
/// Một dòng giỏ hàng ở dạng THUẦN LƯU TRỮ: chỉ mã sản phẩm và số lượng.
///
/// <para>
/// Không phải entity: không có Id, không có vòng đời riêng, hai dòng cùng
/// (ProductId, Quantity) là như nhau. Giỏ trong Session hoàn toàn không có
/// <see cref="Entities.CartItem"/> nào, nên hợp đồng chung giữa hai kho lưu trữ
/// phải là kiểu này chứ không phải entity.
/// </para>
/// <para>
/// CỐ Ý không mang tên, giá, ảnh sản phẩm. Dữ liệu đó luôn đến từ bảng Products
/// dù giỏ nằm ở đâu, nên <c>CartService</c> nạp một lần cho cả hai kho. Nhét
/// vào đây thì <c>SessionCartStore</c> sẽ phải tự truy vấn Products - tức là
/// viết lần thứ hai đúng đoạn mà việc tách <c>ICartStore</c> sinh ra để tránh.
/// </para>
/// </summary>
public sealed record CartLine(int ProductId, int Quantity);
