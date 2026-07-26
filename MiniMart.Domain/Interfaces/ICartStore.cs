using MiniMart.Domain.ValueObjects;

namespace MiniMart.Domain.Interfaces;

/// <summary>
/// Nơi CẤT giỏ hàng. Có đúng hai cài đặt: <c>DatabaseCartStore</c> cho người đã
/// đăng nhập và <c>SessionCartStore</c> cho khách vãng lai. Việc chọn cái nào
/// xảy ra MỘT LẦN duy nhất trong đăng ký DI, không phải mỗi lần gọi API.
///
/// <para>
/// Interface được giữ mỏng có chủ đích - mọi thứ khai báo ở đây đều phải viết
/// HAI lần. Vì vậy nó chỉ biết cất và lấy <see cref="CartLine"/>; toàn bộ
/// nghiệp vụ (cộng dồn khi thêm trùng, kẹp theo tồn kho, nạp tên/giá sản phẩm)
/// nằm ở <c>CartService</c> - viết một lần, dùng cho cả hai kho.
/// </para>
/// <para>
/// KHÔNG có <c>SaveAsync</c>: quy ước dự án đặt <c>SaveChangesAsync</c> ở
/// <c>IUnitOfWork</c>, và thêm lại vào đây sẽ nói dối - cùng một DbContext dùng
/// chung nên "lưu giỏ hàng" thực chất commit thay đổi của mọi repository khác.
/// Hệ quả có thật: <c>DatabaseCartStore</c> chỉ đánh dấu thay đổi và chờ Service
/// gọi <c>SaveChangesAsync</c>, còn <c>SessionCartStore</c> ghi vào Session NGAY.
/// Bất đối xứng này chấp nhận được vì mỗi endpoint giỏ hàng chỉ thực hiện đúng
/// MỘT thao tác ghi; nếu sau này có endpoint ghi nhiều bước thì phải xem lại.
/// </para>
/// </summary>
public interface ICartStore
{
    /// <summary>Toàn bộ dòng trong giỏ. Giỏ rỗng trả danh sách rỗng, không trả null.</summary>
    Task<IReadOnlyList<CartLine>> GetLinesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Đặt số lượng cho một sản phẩm, tạo dòng mới nếu chưa có.
    /// </summary>
    /// <param name="quantity">
    /// Phải >= 1. Số 0 KHÔNG có nghĩa là xoá ở đây - dùng <see cref="RemoveAsync"/>.
    /// Để 0 ngầm hiểu là xoá thì cùng một ý định có hai đường thực hiện, và DB
    /// đã có <c>CHECK Quantity > 0</c> nên số 0 cũng không cất được.
    /// </param>
    Task SetQuantityAsync(int productId, int quantity, CancellationToken cancellationToken = default);

    Task RemoveAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>Xoá sạch giỏ. Dùng khi gộp giỏ Session vào DB lúc đăng nhập.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
