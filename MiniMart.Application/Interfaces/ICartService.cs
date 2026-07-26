using MiniMart.Application.Models;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Application.Interfaces;

/// <summary>
/// Nghiệp vụ giỏ hàng. Viết MỘT lần, chạy cho cả giỏ Session (khách vãng lai) và
/// giỏ DB (đã đăng nhập) - vì nó chỉ nói chuyện với <c>ICartStore</c>, không biết
/// dữ liệu thực sự nằm ở đâu.
/// </summary>
public interface ICartService
{
    /// <summary>
    /// Giỏ hàng kèm dữ liệu sản phẩm hiện tại.
    ///
    /// <para>
    /// Dòng nào trỏ tới sản phẩm KHÔNG CÒN TỒN TẠI sẽ bị lọc khỏi kết quả nhưng
    /// KHÔNG bị xoá khỏi kho: đây là đường đọc, mà GET phải là thao tác an toàn.
    /// Giỏ DB tự sạch nhờ khoá ngoại CASCADE; giỏ Session thì giữ lại id chết cho
    /// tới khi người dùng bấm xoá hoặc phiên hết hạn.
    /// </para>
    /// </summary>
    Task<CartView> GetCartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cộng thêm vào số lượng ĐANG CÓ trong giỏ (không phải đặt tuyệt đối).
    ///
    /// <para>
    /// Nếu tổng vượt tồn kho thì KẸP về đúng số còn lại và trả kèm
    /// <c>Notice</c> giải thích - hữu ích hơn là từ chối rồi để người dùng tự
    /// đoán còn bao nhiêu.
    /// </para>
    /// </summary>
    /// <exception cref="MiniMart.Common.Exceptions.NotFoundException">Sản phẩm không tồn tại.</exception>
    /// <exception cref="MiniMart.Common.Exceptions.OutOfStockException">Tồn kho = 0.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quantity"/> &lt; 1.</exception>
    Task<CartMutationResult> AddAsync(
        int productId,
        int quantity = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đặt số lượng TUYỆT ĐỐI cho một sản phẩm. <paramref name="quantity"/> = 0
    /// nghĩa là xoá dòng - đây là ngữ nghĩa của ô nhập số lượng trên trang giỏ hàng,
    /// khác hẳn <see cref="AddAsync"/>.
    /// </summary>
    /// <exception cref="MiniMart.Common.Exceptions.NotFoundException">Sản phẩm không tồn tại.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quantity"/> &lt; 0.</exception>
    Task<CartMutationResult> UpdateQuantityAsync(
        int productId,
        int quantity,
        CancellationToken cancellationToken = default);

    /// <summary>Xoá một dòng. Xoá thứ không có trong giỏ là thành công, không phải lỗi.</summary>
    Task<CartMutationResult> RemoveAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gộp giỏ nguồn (Session) vào giỏ đích (DB) rồi xoá giỏ nguồn.
    /// Dùng đúng một chỗ: ngay sau khi đăng nhập / đăng ký thành công.
    ///
    /// <para>
    /// Nhận hai <c>ICartStore</c> TƯỜNG MINH thay vì dùng kho do factory chọn, vì
    /// tại thời điểm gộp thì factory chọn SAI: <c>SignInAsync</c> chỉ phát cookie
    /// cho response, nó KHÔNG cập nhật <c>HttpContext.User</c> của request đang
    /// chạy - nên <c>ICurrentUser.IsAuthenticated</c> vẫn là false và factory vẫn
    /// trả về kho Session. Gộp vốn dĩ là thao tác trên HAI kho cụ thể, nên truyền
    /// vào là đúng bản chất chứ không phải cách lách.
    /// </para>
    /// </summary>
    /// <param name="nguon">Kho chứa giỏ của khách vãng lai.</param>
    /// <param name="dich">Kho chứa giỏ của người vừa đăng nhập.</param>
    Task MergeAsync(
        ICartStore nguon,
        ICartStore dich,
        CancellationToken cancellationToken = default);
}
