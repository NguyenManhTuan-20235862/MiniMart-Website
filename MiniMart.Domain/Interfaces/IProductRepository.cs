using MiniMart.Common;
using MiniMart.Domain.Entities;
using MiniMart.Domain.ValueObjects;

namespace MiniMart.Domain.Interfaces;

public interface IProductRepository
{
    /// <summary>
    /// Danh sách sản phẩm có lọc và phân trang. Mọi tham số lọc đều tuỳ chọn:
    /// null nghĩa là không áp dụng điều kiện đó.
    /// </summary>
    Task<PagedResult<Product>> GetProductsAsync(
        int? categoryId = null,
        string? search = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int page = 1,
        int pageSize = 12,
        CancellationToken cancellationToken = default);

    /// <summary>Danh sách để hiển thị - kèm Category, không theo dõi thay đổi.</summary>
    Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<List<Product>> GetByCategoryAsync(int categoryId, CancellationToken cancellationToken = default);

    /// <summary>Bản chỉ đọc dùng cho trang chi tiết.</summary>
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhiều sản phẩm trong MỘT truy vấn. Dùng cho giỏ hàng: gọi
    /// <see cref="GetByIdAsync"/> cho từng dòng là 12 round-trip cho giỏ 12 món
    /// (bài toán N+1). Trả về ít phần tử hơn số id truyền vào nếu có sản phẩm
    /// đã bị xoá - người gọi phải xử lý trường hợp đó.
    /// </summary>
    Task<List<Product>> GetByIdsAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vài sản phẩm CÙNG DANH MỤC để gợi ý ở cuối trang chi tiết, trừ chính sản
    /// phẩm đang xem.
    ///
    /// <para>
    /// Sản phẩm CÒN HÀNG được xếp lên trước: gợi ý một món không mua được là gợi ý
    /// vô ích. Đây là quy tắc nghiệp vụ nhưng nó được diễn đạt bằng <c>ORDER BY</c>,
    /// nên chỗ đúng của nó là truy vấn — kéo hết danh mục về rồi sắp trong C# là
    /// đúng cùng con số trên màn hình với một hoá đơn database khác hẳn.
    /// </para>
    /// <para>
    /// <c>Take</c> cần tie-breaker duy nhất y hệt <c>Skip</c>/<c>Take</c> của phân
    /// trang: thiếu nó thì hai lần tải cùng một trang có thể ra hai bộ gợi ý khác
    /// nhau, và không có gì báo lỗi.
    /// </para>
    /// </summary>
    Task<List<Product>> GetRelatedAsync(
        int productId,
        int categoryId,
        int take = 4,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gợi ý cho ô tìm kiếm ở header: vài sản phẩm có tên CHỨA từ khoá, xếp theo mức
    /// độ khớp giảm dần.
    ///
    /// <para>
    /// Thứ tự xếp hạng — đây là phần "giống nhất lên trước", và nó là quy tắc nghiệp
    /// vụ chứ không phải chi tiết truy vấn:
    /// </para>
    /// <list type="number">
    /// <item>tên <b>bắt đầu bằng</b> từ khoá (gõ "iph" → "iPhone 16" trước);</item>
    /// <item>một <b>từ</b> trong tên bắt đầu bằng từ khoá ("pro" → "MacBook Pro 14");</item>
    /// <item>khớp ở giữa một từ.</item>
    /// </list>
    /// <para>
    /// Đồng hạng thì <b>tên ngắn hơn lên trước</b>: từ khoá chiếm tỉ lệ lớn hơn trong
    /// một tên ngắn, nên nó "giống" hơn theo cảm nhận của người gõ.
    /// </para>
    /// <para>
    /// So sánh KHÔNG phân biệt dấu — người Việt gõ nhanh thường bỏ dấu, và collation
    /// mặc định của database là <c>..._CI_AS</c> (phân biệt dấu) nên "chuot" trả về
    /// rỗng dù shop đang bán "Chuột Logitech". Đã đo trực tiếp trên SQL Server.
    /// </para>
    /// </summary>
    /// <param name="tuKhoa">Rỗng hoặc quá ngắn thì trả về danh sách rỗng, không phải toàn bộ kho.</param>
    Task<List<ProductSuggestion>> SuggestAsync(
        string? tuKhoa,
        int take = 8,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bản CÓ theo dõi thay đổi, dùng cho luồng sửa. Phải tách khỏi GetByIdAsync
    /// vì entity AsNoTracking sửa xong gọi SaveChanges sẽ không lưu được gì.
    /// </summary>
    Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhiều sản phẩm CÓ theo dõi thay đổi trong MỘT truy vấn - dùng cho đặt hàng.
    ///
    /// <para>
    /// Bản chùm của <see cref="GetForUpdateAsync"/>. Gọi bản lẻ cho từng dòng giỏ
    /// hàng cũng đúng về concurrency (<c>RowVersion</c> là của từng dòng nên đọc lẻ
    /// hay đọc chùm đều lấy đúng giá trị hiện tại), nhưng là N round-trip cho giỏ N
    /// món.
    /// </para>
    /// <para>
    /// Trả về ít phần tử hơn số id truyền vào nếu có sản phẩm đã bị xoá - người gọi
    /// PHẢI xử lý trường hợp đó.
    /// </para>
    /// </summary>
    Task<List<Product>> GetManyForUpdateAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Khai báo phiên bản mà người dùng ĐÃ THẤY lúc mở form, để lần lưu tới so
    /// với phiên bản đó thay vì phiên bản vừa đọc lên.
    ///
    /// <para>
    /// Không có bước này thì Optimistic Concurrency chỉ bảo vệ được khoảng vài
    /// millisecond giữa <see cref="GetForUpdateAsync"/> và lúc lưu - còn khoảng
    /// thật sự cần bảo vệ là vài phút người dùng ngồi điền form thì bỏ trống.
    /// </para>
    /// <para>
    /// Nằm ở Repository vì việc này cần API của tầng lưu trữ (ghi vào
    /// <c>OriginalValue</c> của Change Tracker), thứ mà Application không được
    /// biết đến. Domain chỉ khai báo NHU CẦU "ghim phiên bản mong đợi".
    /// </para>
    /// </summary>
    void SetExpectedRowVersion(Product product, byte[] rowVersion);

    Task AddAsync(Product product, CancellationToken cancellationToken = default);

    void Remove(Product product);
}
