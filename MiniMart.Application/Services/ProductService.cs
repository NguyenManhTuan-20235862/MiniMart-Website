using MiniMart.Application.Interfaces;
using MiniMart.Application.Models;
using MiniMart.Common;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    // Service được phép phối hợp NHIỀU repository - đó chính là lý do quy tắc
    // liên quan tới hai bảng phải nằm ở đây chứ không nằm trong repository nào.
    public ProductService(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public Task<PagedResult<Product>> GetProductsAsync(
        int? categoryId = null,
        string? search = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int page = 1,
        int pageSize = 12,
        CancellationToken cancellationToken = default) =>
        _productRepository.GetProductsAsync(
            categoryId, search, minPrice, maxPrice, page, pageSize, cancellationToken);

    public Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _productRepository.GetAllAsync(cancellationToken);

    public Task<List<Product>> GetByCategoryAsync(
        int categoryId,
        CancellationToken cancellationToken = default) =>
        _productRepository.GetByCategoryAsync(categoryId, cancellationToken);

    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _productRepository.GetByIdAsync(id, cancellationToken);

    public Task<List<Product>> GetRelatedAsync(
        int productId,
        int categoryId,
        int take = 4,
        CancellationToken cancellationToken = default) =>
        _productRepository.GetRelatedAsync(productId, categoryId, take, cancellationToken);

    public Task<List<Product>> GetByIdsAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken = default) =>
        _productRepository.GetByIdsAsync(ids, cancellationToken);

    public async Task<Product> CreateAsync(
        string name,
        decimal price,
        int stock,
        int categoryId,
        string? imageUrl = null,
        CancellationToken cancellationToken = default)
    {
        await BaoDamDanhMucTonTaiAsync(categoryId, cancellationToken);

        var product = new Product
        {
            Name = name.Trim(),
            Price = price,
            Stock = stock,
            CategoryId = categoryId,
            ImageUrl = imageUrl
        };

        await _productRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return product;
    }

    public async Task UpdateAsync(
        int id,
        string name,
        decimal price,
        int stock,
        int categoryId,
        string? imageUrl = null,
        byte[]? rowVersion = null,
        CancellationToken cancellationToken = default)
    {
        // GetForUpdateAsync (có tracking) chứ không phải GetByIdAsync: entity
        // phải được Change Tracker theo dõi thì RowVersion gốc mới được giữ để
        // kẹp vào WHERE lúc UPDATE.
        var product = await _productRepository.GetForUpdateAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Product), id);

        // Ghim phiên bản người dùng thấy lúc mở form. Không có bước này thì
        // phiên bản được so là phiên bản VỪA ĐỌC ở dòng trên - luôn khớp, nên
        // xung đột không bao giờ bị phát hiện (lost update).
        if (rowVersion is { Length: > 0 })
        {
            _productRepository.SetExpectedRowVersion(product, rowVersion);
        }

        await BaoDamDanhMucTonTaiAsync(categoryId, cancellationToken);

        product.Name = name.Trim();
        product.Price = price;
        product.Stock = stock;
        product.CategoryId = categoryId;

        // Chỉ ghi đè khi có ảnh mới. imageUrl = null nghĩa là người dùng không
        // chọn file, phải GIỮ ảnh cũ chứ không phải xoá nó.
        if (imageUrl is not null)
        {
            product.ImageUrl = imageUrl;
        }

        // Xung đột RowVersion nổi lên từ đây dưới dạng ConcurrencyConflictException
        // (UnitOfWork đã dịch từ DbUpdateConcurrencyException). CỐ Ý không bắt ở
        // Service: quyết định "ghi đè hay bỏ" là của người dùng, không phải của
        // nghiệp vụ - Controller mới có đủ ngữ cảnh để hỏi lại họ.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetForUpdateAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Product), id);

        // Kiểm TRƯỚC để có thông báo tử tế, đúng khuôn của CategoryService: dựa vào
        // lỗi khoá ngoại của DB thì người dùng nhận được "FK constraint violated".
        //
        // OrderDetails.ProductId là Restrict (đơn hàng là bản ghi tài chính), khác
        // hẳn CartItems.ProductId là Cascade (giỏ hàng là dữ liệu tạm).
        if (await _orderRepository.HasOrdersForProductAsync(id, cancellationToken))
        {
            throw new ProductHasOrdersException(id);
        }

        _productRepository.Remove(product);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ReferenceConstraintException ex)
        {
            // Bịt khe TOCTOU: có người vừa đặt hàng đúng sản phẩm này giữa lúc kiểm
            // tra ở trên và lúc lưu. Restrict dưới DB là bảo đảm cuối cùng; đây chỉ
            // là việc dịch nó thành cùng một thông báo nghiệp vụ để người dùng không
            // gặp hai kiểu lỗi khác nhau cho cùng một nguyên nhân.
            throw new ProductHasOrdersException(id, ex);
        }
    }

    public async Task<BulkUpdateResult> BulkUpdatePriceStockAsync(
        IReadOnlyList<ProductBulkUpdateItem> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            // Không round-trip nào cho một danh sách rỗng, và quan trọng hơn: "lưu 0
            // dòng" là một kết cục bình thường (bảng rỗng), không phải lỗi.
            return new BulkUpdateResult(0, []);
        }

        // Hai dòng cùng Id thì cả hai cùng ghi vào MỘT entity trong Change Tracker
        // (identity map: một khoá chính = một object), dòng sau đè dòng trước và
        // KHÔNG có exception nào. Form do server render không tạo ra được tình huống
        // này, nhưng "chỉ xảy ra khi bị chế lại" cộng với "hỏng trong im lặng" là đúng
        // tổ hợp phải chặn tường minh.
        var trung = items.GroupBy(i => i.Id).FirstOrDefault(g => g.Count() > 1);

        if (trung is not null)
        {
            throw new ArgumentException(
                $"Danh sách có {trung.Count()} dòng cùng ProductId = {trung.Key}.", nameof(items));
        }

        // MỘT truy vấn cho cả N dòng (WHERE Id IN (...)), không phải N lần
        // GetForUpdateAsync. Có tracking - đó là toàn bộ lý do phải dùng
        // GetManyForUpdateAsync chứ không phải GetByIdsAsync.
        var products = await _productRepository.GetManyForUpdateAsync(
            items.Select(i => i.Id), cancellationToken);

        var theoId = products.ToDictionary(p => p.Id);
        var xungDot = new List<ProductConflict>();

        foreach (var item in items)
        {
            if (!theoId.TryGetValue(item.Id, out var product))
            {
                // Sản phẩm biến mất giữa lúc bảng được mở và lúc bấm Lưu. Cùng LOẠI
                // với RowVersion lệch - "người khác đã thay đổi thứ bạn đang nhìn" -
                // nên đi chung một đường báo cáo, chỉ khác ở chỗ không có giá trị hiện
                // tại nào để nêu.
                xungDot.Add(new ProductConflict(item.Id, $"(Id {item.Id})", 0m, 0, null));
                continue;
            }

            // ── Lớp 0: dòng người dùng KHÔNG sửa thì không có việc gì để làm ───────
            //
            // Và quan trọng hơn: nó cũng KHÔNG phải xung đột, dù RowVersion đã đổi.
            // Bảng này chỉ có hai ô Giá và Tồn kho, nên người khác đổi TÊN một sản phẩm
            // là RowVersion dòng đó nhảy trong khi không có gì ta định ghi bị ảnh hưởng.
            // Báo xung đột ở đây là bắt Admin đi xem lại một dòng mà chính họ không
            // chạm vào - nhiễu, và làm họ quen với việc bỏ qua thông báo.
            //
            // Đây cũng chính là tính chất mà đường change tracking cho không: gán một
            // giá trị bằng đúng giá trị đang có không sinh câu UPDATE nào. Lệnh kiểm này
            // chỉ làm cho nó TƯỜNG MINH và áp dụng được trước cả bước so phiên bản.
            if (product.Price == item.Price && product.Stock == item.Stock)
            {
                continue;
            }

            // ── Lớp 1: bịt cửa sổ RỘNG (vài phút bảng mở) ──────────────────────────
            //
            // So phiên bản form gửi lên với phiên bản VỪA ĐỌC LÊN. Đây là lệnh kiểm duy
            // nhất có thể bỏ qua CHỌN LỌC từng dòng, vì nó chạy trước khi có câu UPDATE
            // nào được sinh ra. Đợi tới SaveChanges thì EF ném cho cả batch và không còn
            // cách nào cứu 18 dòng còn lại.
            if (item.RowVersion is { Length: > 0 }
                && product.RowVersion is not null
                && !product.RowVersion.SequenceEqual(item.RowVersion))
            {
                xungDot.Add(new ProductConflict(
                    product.Id, product.Name, product.Price, product.Stock, product.RowVersion));

                // ★ TUYỆT ĐỐI không chạm entity ở nhánh này. Gán Price/Stock rồi mới
                // `continue` là EF vẫn sinh câu UPDATE cho nó, và cả batch quay về
                // all-or-nothing - đúng thứ vừa bỏ công tránh.
                continue;
            }

            // ── Lớp 2: bịt cửa sổ HẸP (giữa lệnh đọc ở trên và SaveChanges bên dưới) ─
            //
            // Lớp 1 một mình là TOCTOU kinh điển: giữa lúc so sánh và lúc ghi vẫn còn
            // một khe. Ghim phiên bản mong đợi để SQL Server kẹp nó vào WHERE - đó mới
            // là bảo đảm thật, đúng khuôn "kiểm ở Service để có thông báo tử tế, ràng
            // buộc ở DB để có sự thật".
            if (item.RowVersion is { Length: > 0 })
            {
                _productRepository.SetExpectedRowVersion(product, item.RowVersion);
            }

            // Gán giá trị BẰNG giá trị cũ không đánh dấu Modified (EF Core so sánh với
            // OriginalValue), nên dòng không sửa gì sẽ không sinh câu UPDATE nào.
            product.Price = item.Price;
            product.Stock = item.Stock;
        }

        // KHÔNG mở transaction tường minh, và đây là quyết định chứ không phải bỏ sót.
        //
        // Vẫn CÓ transaction: EF Core tự bọc mỗi SaveChanges trong một transaction ngầm.
        // Điều đổi so với bản trước là ĐƠN VỊ nguyên tử: nó bao "các dòng sống sót sau
        // lớp 1", không phải "mọi dòng người dùng gửi lên" - và đó chính là ngữ nghĩa
        // được yêu cầu. Dòng bị bỏ qua không nằm trong đó vì nó không sinh lệnh ghi nào.
        //
        // Thêm BeginTransactionAsync ở đây không bảo vệ thêm gì (chỉ một SaveChanges,
        // không có thao tác ghi thứ hai nào đang chờ tới) và sẽ ngụ ý rằng nó có.
        //
        // ConcurrencyConflictException từ đây CHỈ còn có thể đến từ cửa sổ hẹp. Cố ý
        // không bắt: đó là trường hợp cả batch bị bỏ, và chỉ Controller mới nói lại
        // được với người dùng là hãy bấm Lưu lần nữa.
        var soDongDaLuu = await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new BulkUpdateResult(soDongDaLuu, xungDot);
    }

    /// <summary>
    /// Không dựa vào lỗi khoá ngoại của DB: thông báo "FK constraint violated"
    /// vô nghĩa với người dùng cuối.
    /// </summary>
    private async Task BaoDamDanhMucTonTaiAsync(int categoryId, CancellationToken cancellationToken)
    {
        if (await _categoryRepository.GetByIdAsync(categoryId, cancellationToken) is null)
        {
            throw new NotFoundException(nameof(Category), categoryId);
        }
    }
}
