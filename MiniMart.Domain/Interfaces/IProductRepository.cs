using MiniMart.Common;
using MiniMart.Domain.Entities;

namespace MiniMart.Domain.Interfaces;

public interface IProductRepository
{
    /// <summary>
    /// Danh sách sản phẩm có lọc và phân trang. Mọi tham số lọc đều tuỳ chọn:
    /// null nghĩa là không áp dụng điều kiện đó.
    /// </summary>
    Task<PagedResult<Product>> GetProductsAsync(
        int? categoryId = null,
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
