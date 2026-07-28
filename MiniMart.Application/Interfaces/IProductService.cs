using MiniMart.Application.Models;
using MiniMart.Common;
using MiniMart.Domain.Entities;

namespace MiniMart.Application.Interfaces;

public interface IProductService
{
    /// <summary>
    /// Danh sách sản phẩm cho trang khách hàng: có lọc và phân trang.
    /// Hiện là pass-through xuống Repository, nhưng vẫn phải đi qua Service vì
    /// đây là chỗ các quy tắc nghiệp vụ sẽ được thêm vào (ẩn sản phẩm hết hàng,
    /// giá theo nhóm khách...) mà không phải sửa Controller.
    /// </summary>
    Task<PagedResult<Product>> GetProductsAsync(
        int? categoryId = null,
        string? search = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int page = 1,
        int pageSize = 12,
        CancellationToken cancellationToken = default);

    Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<List<Product>> GetByCategoryAsync(int categoryId, CancellationToken cancellationToken = default);

    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gợi ý vài sản phẩm cùng danh mục cho cuối trang chi tiết, ưu tiên món còn hàng.
    ///
    /// <para>
    /// Trả về danh sách RỖNG khi danh mục chỉ có đúng sản phẩm đang xem — đó là kết
    /// cục bình thường, không phải lỗi, nên tầng Web chỉ việc không hiển thị khu vực
    /// gợi ý chứ không phải xử lý ngoại lệ nào.
    /// </para>
    /// </summary>
    Task<List<Product>> GetRelatedAsync(
        int productId,
        int categoryId,
        int take = 4,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhiều sản phẩm trong MỘT truy vấn. Trả về ít phần tử hơn số id truyền vào nếu
    /// có sản phẩm đã bị xoá — người gọi phải xử lý trường hợp đó.
    /// </summary>
    Task<List<Product>> GetByIdsAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken = default);

    Task<Product> CreateAsync(
        string name,
        decimal price,
        int stock,
        int categoryId,
        string? imageUrl = null,
        CancellationToken cancellationToken = default);

    /// <param name="imageUrl">
    /// null nghĩa là GIỮ NGUYÊN ảnh cũ, không phải xoá ảnh. Người dùng không
    /// chọn file mới thì ảnh hiện tại phải được giữ lại.
    /// </param>
    /// <param name="rowVersion">
    /// Phiên bản người dùng thấy lúc MỞ form, do form gửi lại qua hidden field.
    /// null = bỏ qua kiểm tra xung đột (dùng cho luồng nội bộ không có form).
    /// </param>
    /// <exception cref="MiniMart.Common.Exceptions.ConcurrencyConflictException">
    /// Bản ghi đã bị người khác sửa hoặc xoá sau khi form được mở.
    /// </exception>
    /// <exception cref="MiniMart.Common.Exceptions.NotFoundException">
    /// Không tìm thấy sản phẩm, hoặc danh mục được chọn không tồn tại.
    /// </exception>
    Task UpdateAsync(
        int id,
        string name,
        decimal price,
        int stock,
        int categoryId,
        string? imageUrl = null,
        byte[]? rowVersion = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sửa giá và tồn kho của NHIỀU sản phẩm trong một lần lưu, mỗi dòng một giá trị
    /// riêng và mỗi dòng một <c>RowVersion</c> riêng.
    ///
    /// <para>
    /// <b>Thành công một phần là kết cục HỢP LỆ ở đây.</b> Dòng nào có
    /// <c>RowVersion</c> lệch thì bị BỎ QUA và báo lại qua
    /// <see cref="BulkUpdateResult.XungDot"/>; các dòng còn lại vẫn được ghi. Đây là
    /// khác biệt CÓ CHỦ Ý so với <c>OrderService.CheckoutAsync</c>, nơi một dòng hỏng
    /// phải huỷ cả đơn: ở đó nửa vời nghĩa là khách trả tiền cho một đơn không đúng
    /// thứ họ đặt, còn ở đây người dùng là Admin đang nhìn màn hình, và "18 dòng đã
    /// lưu, 2 dòng cần xem lại" là một câu họ xử lý được ngay.
    /// </para>
    /// <para>
    /// <b>Hai lớp kiểm, mỗi lớp bịt một cửa sổ khác nhau</b> — bỏ lớp nào cũng sai:
    /// </para>
    /// <list type="number">
    /// <item>
    /// So <c>RowVersion</c> trong bộ nhớ (giá trị vừa đọc lên với giá trị form gửi lên)
    /// bịt cửa sổ RỘNG — vài phút bảng mở. Đây là thứ cho phép bỏ qua CHỌN LỌC, vì nó
    /// chạy trước khi có câu UPDATE nào được sinh ra.
    /// </item>
    /// <item>
    /// <c>SetExpectedRowVersion</c> + <c>WHERE RowVersion = @original</c> bịt cửa sổ
    /// HẸP — vài mili giây giữa lệnh đọc và lệnh ghi. Lớp 1 một mình là TOCTOU kinh
    /// điển; lớp này là bảo đảm thật.
    /// </item>
    /// </list>
    /// <para>
    /// Hệ quả của cửa sổ hẹp: nếu ai đó ghi ĐÚNG vào khoảng vài mili giây đó thì cả
    /// batch bị bỏ và <c>ConcurrencyConflictException</c> nổi lên — tức trường hợp hiếm
    /// này VẪN là tất-cả-hoặc-không-gì-cả. Không tự thử lại: thử lại sạch sẽ đòi một
    /// <c>DbContext</c> mới nên không làm được trong cùng request (cùng lý do đã hoãn
    /// retry cho đua tạo giỏ hàng lần đầu).
    /// </para>
    /// <para>
    /// Dòng người dùng KHÔNG sửa thì không sinh câu <c>UPDATE</c> nào, nên
    /// <c>RowVersion</c> cũ ở dòng đó cũng không bị coi là xung đột. Đã đo: chạm 4
    /// entity mà chỉ đổi giá trị của 3 thì batch chỉ có 3 câu UPDATE.
    /// </para>
    /// </summary>
    /// <exception cref="System.ArgumentException">
    /// Danh sách có hai dòng cùng một <c>Id</c>. Form hợp lệ không tạo ra được tình
    /// huống này; nếu bỏ qua thì dòng sau âm thầm đè dòng trước.
    /// </exception>
    /// <exception cref="MiniMart.Common.Exceptions.ConcurrencyConflictException">
    /// CHỈ cho cửa sổ hẹp nói trên. Xung đột thông thường KHÔNG ném — nó nằm trong
    /// <see cref="BulkUpdateResult.XungDot"/>.
    /// </exception>
    Task<BulkUpdateResult> BulkUpdatePriceStockAsync(
        IReadOnlyList<ProductBulkUpdateItem> items,
        CancellationToken cancellationToken = default);
}
