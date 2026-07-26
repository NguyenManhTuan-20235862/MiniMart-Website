using MiniMart.Application.Interfaces;
using MiniMart.Application.Models;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Application.Services;

/// <summary>
/// Đặt hàng: chốt giỏ hàng thành đơn, trừ tồn kho, snapshot giá.
///
/// <para>
/// Đây là nghiệp vụ duy nhất trong dự án cần transaction, và là lý do
/// <c>IUnitOfWork.BeginTransactionAsync</c> được thêm vào đúng lúc này.
/// </para>
/// <para>
/// Chống oversell bằng <b>Optimistic Concurrency</b> trên <c>Products.RowVersion</c>:
/// đọc sản phẩm kèm RowVersion, trừ tồn kho trong bộ nhớ, và khi lưu thì EF Core tự
/// thêm <c>WHERE RowVersion = @original</c>. Ai ghi sau sẽ khớp 0 dòng và bị từ chối
/// thay vì ghi đè phép trừ của người trước (lost update).
/// </para>
/// <para>
/// Đã cân nhắc Pessimistic (<c>UPDLOCK</c>): nó cho người mua sau CHỜ thay vì báo
/// lỗi, tốt hơn khi tranh chấp cao. Chọn Optimistic vì hai cách đúng như nhau về
/// tính đúng đắn, còn hạ tầng RowVersion thì dự án đã có và đã dùng cho luồng Admin -
/// đưa mô hình thứ hai vào cùng một bảng khó suy luận hơn nhiều so với giữ một mô hình.
/// </para>
/// <para>
/// Lớp bảo vệ thứ hai không phụ thuộc chiến lược: CHECK constraint <c>Stock >= 0</c>
/// dưới DB. Kể cả khi logic ở đây sai, database vẫn không cho tồn kho âm.
/// </para>
/// </summary>
public class OrderService : IOrderService
{
    private readonly ICartStore _cartStore;
    private readonly IProductRepository _productRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public OrderService(
        ICartStore cartStore,
        IProductRepository productRepository,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork)
    {
        _cartStore = cartStore;
        _productRepository = productRepository;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<CheckoutResult> CheckoutAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var lines = await _cartStore.GetLinesAsync(cancellationToken);

        if (lines.Count == 0)
        {
            throw new EmptyCartException();
        }

        // Transaction mở TRƯỚC lần đọc sản phẩm, không phải trước lần ghi: đọc nằm
        // ngoài transaction thì phép trừ được tính trên dữ liệu của một snapshot
        // khác với snapshot lúc ghi.
        //
        // await using: không commit thì DisposeAsync rollback. Nhờ vậy mọi đường
        // thoát bằng exception ở dưới đều tự rollback, không cần try/catch nào chỉ
        // để dọn dẹp.
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // Sắp xếp theo ProductId TRƯỚC khi xử lý. Lý do là deadlock: hai đơn cùng
        // chứa sản phẩm A và B mà một bên khoá A->B còn bên kia B->A thì hai
        // transaction chờ nhau vĩnh viễn và SQL Server phải hạ một bên. Mọi
        // transaction chạm nhiều dòng phải chạm chúng theo CÙNG một thứ tự.
        //
        // Với Optimistic thì lock nhẹ hơn Pessimistic, nhưng UPDATE vẫn giữ lock
        // tới cuối transaction nên rủi ro vẫn thật. Sắp xếp là cách rẻ nhất để loại
        // hẳn nó.
        var thuTu = lines.OrderBy(l => l.ProductId).ToList();

        // MỘT truy vấn cho cả giỏ, có tracking + RowVersion. Gọi GetForUpdateAsync
        // cho từng dòng cũng đúng về concurrency (RowVersion là của từng dòng, đọc
        // lẻ hay đọc chùm đều lấy đúng giá trị hiện tại) nhưng là N round-trip cho
        // giỏ N món.
        var sanPham = await _productRepository.GetManyForUpdateAsync(
            thuTu.Select(l => l.ProductId), cancellationToken);

        var theoId = sanPham.ToDictionary(p => p.Id);

        var order = new Order
        {
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var line in thuTu)
        {
            if (!theoId.TryGetValue(line.ProductId, out var product))
            {
                // Sản phẩm đã bị xoá khỏi shop trong lúc giỏ còn sống. KHÔNG bỏ qua
                // im lặng như lúc gộp giỏ: ở đó bỏ một dòng là chuyện nhỏ, còn ở đây
                // người dùng vừa bấm "xác nhận" trên một tổng tiền cụ thể - lặng lẽ
                // đặt đơn ít hàng hơn và thu số tiền khác là điều không được làm.
                throw new NotFoundException(nameof(Product), line.ProductId);
            }

            // Kiểm tra Ở ĐÂY để có thông báo tử tế kèm số lượng còn lại. Nó KHÔNG
            // phải thứ chống oversell - giữa dòng này và lúc SaveChanges vẫn còn khe
            // TOCTOU. Việc chống oversell là của RowVersion (và CHECK dưới DB).
            if (product.Stock < line.Quantity)
            {
                throw new InsufficientStockException(
                    product.Name, product.Stock, line.Quantity);
            }

            // Phép trừ này chỉ nằm trong bộ nhớ. Nó chỉ thành sự thật khi SaveChanges
            // chạy được câu UPDATE có WHERE RowVersion khớp.
            product.Stock -= line.Quantity;

            order.Items.Add(new OrderDetail
            {
                ProductId = product.Id,

                // SNAPSHOT: chốt tên và giá tại đúng thời điểm này. Đọc lại đơn hàng
                // tháng sau phải ra đúng con số hôm nay, kể cả khi shop đã đổi giá.
                ProductName = product.Name,
                UnitPrice = product.Price,

                Quantity = line.Quantity
            });
        }

        // Tổng tính từ giá ĐÃ snapshot, không gọi lại product.Price: hai chỗ đọc giá
        // là hai cơ hội để tổng đơn lệch khỏi tổng các dòng.
        order.TotalAmount = order.Items.Sum(i => i.LineTotal);

        await _orderRepository.AddAsync(order, cancellationToken);

        // Xoá giỏ trong CÙNG transaction. Ngoài transaction thì lưu đơn thất bại sẽ
        // để lại người dùng không có đơn mà cũng không còn giỏ.
        await _cartStore.ClearAsync(cancellationToken);

        try
        {
            // Một SaveChanges cho tất cả: UPDATE tồn kho từng sản phẩm, INSERT Order,
            // INSERT các OrderDetail, DELETE các CartItem.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException ex)
        {
            // ĐÂY là chỗ chống oversell thật sự.
            //
            // ConcurrencyConflictException chính là DbUpdateConcurrencyException đã
            // được UnitOfWork dịch tầng - Application không được using EF Core nên
            // không bắt được kiểu gốc. Bắt RIÊNG nó (trước mọi exception khác) vì nó
            // có một cách xử lý riêng: đây không phải lỗi hệ thống mà là "có người
            // mua trước bạn".
            //
            // Rollback tường minh cho rõ ý, dù await using cũng đã làm điều đó.
            await transaction.RollbackAsync(cancellationToken);

            throw new InsufficientStockException(TenSanPhamXungDot(ex, theoId), ex);
        }

        await transaction.CommitAsync(cancellationToken);

        return new CheckoutResult(order.Id, order.TotalAmount, order.Items.Count);
    }

    public async Task<OrderView?> GetMyOrderAsync(
        int orderId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        // Lọc theo userId nằm trong TRUY VẤN (xem OrderRepository), không phải kiểm
        // tra sau khi đọc lên.
        var order = await _orderRepository.GetByIdForUserAsync(orderId, userId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        return new OrderView(
            order.Id,
            order.CreatedAt,
            order.TotalAmount,
            order.Items
                .Select(i => new OrderLineView(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity))
                .ToList());
    }

    /// <summary>
    /// Lấy tên sản phẩm gây xung đột từ id mà <see cref="ConcurrencyConflictException"/>
    /// mang theo.
    ///
    /// <para>
    /// Tra trong dictionary đã đọc sẵn chứ không truy vấn lại DB: truy vấn ở đây là
    /// truy vấn sau khi transaction đã rollback, và câu trả lời cũng không dùng để
    /// làm gì ngoài việc ghép vào thông báo.
    /// </para>
    /// <para>
    /// Không tra được thì trả tên chung. Thông báo kém đẹp hơn nhưng vẫn đúng, và
    /// tuyệt đối không được ném exception mới ở trong nhánh xử lý exception.
    /// </para>
    /// </summary>
    private static string TenSanPhamXungDot(
        ConcurrencyConflictException ex,
        Dictionary<int, Product> theoId) =>
        ex.Id is int id && theoId.TryGetValue(id, out var product)
            ? product.Name
            : "trong giỏ hàng";
}
