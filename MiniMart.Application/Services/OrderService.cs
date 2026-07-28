using Microsoft.Extensions.Logging;
using MiniMart.Application.Interfaces;
using MiniMart.Application.Models;
using MiniMart.Common;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Enums;
using MiniMart.Domain.Interfaces;
using MiniMart.Domain.ValueObjects;

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
    private readonly ILogger<OrderService> _logger;

    /// <param name="logger">
    /// <c>ILogger</c> đến từ <c>Microsoft.Extensions.Logging.Abstractions</c> - một
    /// abstraction, không phải hạ tầng, nên tiêm nó vào Application KHÔNG vi phạm DIP.
    /// Cùng loại với <c>TimeProvider</c>: Application khai báo thứ nó cần, còn ai ghi
    /// log ra đâu (Console, file, Seq) là chuyện của Composition Root.
    /// </param>
    public OrderService(
        ICartStore cartStore,
        IProductRepository productRepository,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ILogger<OrderService> logger)
    {
        _cartStore = cartStore;
        _productRepository = productRepository;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<CheckoutResult> CheckoutAsync(
        int userId,
        ShippingInfo shipping,
        CancellationToken cancellationToken = default)
    {
        // Chuẩn hoá rồi kiểm TRƯỚC khi chạm giỏ hàng và TRƯỚC khi mở transaction:
        // đây là kiểm tra thuần bộ nhớ, không tốn một round-trip DB nào. Đặt nó sau
        // BeginTransaction là mở một transaction chỉ để lập tức đóng lại - và trong
        // lúc đó vẫn giữ một connection của pool.
        var giaoHang = shipping.ChuanHoa();

        if (giaoHang.ThieuThongTin)
        {
            throw new ArgumentException(
                "Thiếu thông tin giao hàng (tên người nhận, số điện thoại, địa chỉ).",
                nameof(shipping));
        }

        var lines = await _cartStore.GetLinesAsync(cancellationToken);

        if (lines.Count == 0)
        {
            throw new EmptyCartException();
        }

        // ★ Log theo TEMPLATE có tham số ({UserId}), KHÔNG nội suy chuỗi ($"...").
        //
        // Khác biệt không phải hình thức: với template, Serilog lưu UserId và SoDong
        // thành hai THUỘC TÍNH riêng bên cạnh câu chữ, nên sau này lọc được
        // "mọi lần checkout của user 42" mà không phải parse chuỗi. Nội suy thì mọi
        // dòng là một chuỗi khác nhau - không nhóm được, không đếm được, không lọc được.
        //
        // KHÔNG log tên người nhận / số điện thoại / địa chỉ: chúng là dữ liệu cá nhân,
        // mà log thường đi tới nơi có nhiều người đọc hơn database và sống lâu hơn dự
        // kiến. userId + orderId là đủ để lần lại đơn khi cần.
        _logger.LogInformation(
            "Bắt đầu đặt hàng cho người dùng {UserId} với {SoDong} dòng giỏ hàng.",
            userId, lines.Count);

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
            CreatedAt = DateTime.UtcNow,

            // SNAPSHOT thứ hai của đơn hàng, cùng tinh thần với ProductName/UnitPrice
            // ở OrderDetail: chép GIÁ TRỊ vào đơn thay vì trỏ tới nơi nó có thể đổi.
            RecipientName = giaoHang.RecipientName,
            RecipientPhone = giaoHang.RecipientPhone,
            ShippingAddress = giaoHang.Address
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

            var tenSanPham = TenSanPhamXungDot(ex, theoId);

            // ★ Warning, KHÔNG phải Error - và đây là quyết định, không phải sở thích.
            //
            // Xung đột ở đây nghĩa là "có người mua trước bạn": cơ chế chống oversell
            // vừa hoạt động ĐÚNG như thiết kế. Ghi Error là đánh đồng nó với sự cố hệ
            // thống, và trên bảng giám sát nó sẽ kêu mỗi lần có hai người cùng mua một
            // món - đúng cách để người trực bắt đầu bỏ qua cảnh báo.
            //
            // Nhưng cũng KHÔNG hạ xuống Information: tần suất tăng đột biến ở đây là
            // tín hiệu thật (hàng sắp hết, hoặc có bot mua gom), và ở Production mức
            // mặc định là Warning nên Information sẽ biến mất hoàn toàn.
            //
            // Không kèm exception object: nguyên nhân đã nằm đủ trong câu chữ, còn
            // stack trace của một kết cục nghiệp vụ bình thường chỉ làm log phình ra.
            _logger.LogWarning(
                "Đặt hàng thất bại do tranh chấp tồn kho: người dùng {UserId}, sản phẩm {TenSanPham}.",
                userId, tenSanPham);

            throw new InsufficientStockException(tenSanPham, ex);
        }

        await transaction.CommitAsync(cancellationToken);

        // Log SAU commit, không phải trước: trước commit thì câu "đã tạo đơn" có thể
        // là nói dối - transaction vẫn còn có thể hỏng.
        _logger.LogInformation(
            "Đã tạo đơn {OrderId} cho người dùng {UserId}: {SoDong} dòng, tổng {TongTien}.",
            order.Id, userId, order.Items.Count, order.TotalAmount);

        return new CheckoutResult(order.Id, order.TotalAmount, order.Items.Count);
    }

    public async Task UpdatePaymentStatusAsync(
        int orderId,
        OrderStatus status,
        CancellationToken cancellationToken = default)
    {
        // GetForUpdateAsync (CÓ tracking) - entity đọc bằng AsNoTracking sửa xong gọi
        // SaveChanges sẽ không lưu gì và không có lỗi nào báo.
        //
        // Chi phí đã biết: người gọi (PaymentService) cũng vừa đọc chính đơn này để
        // đối chiếu số tiền, nên đây là round-trip thứ hai. Chấp nhận được vì luồng
        // IPN có lưu lượng rất thấp, và đổi lại chữ ký giữ đúng dạng (orderId, status)
        // thay vì truyền một entity đang được theo dõi qua lại giữa hai service.
        var order = await _orderRepository.GetForUpdateAsync(orderId, cancellationToken);

        if (order is null)
        {
            // Error chứ không Warning: người gọi duy nhất là luồng IPN, và nó chỉ tới
            // được đây SAU khi đã đọc thành công chính đơn này để đối chiếu số tiền.
            // Đơn biến mất giữa hai lần đọc trong cùng một transaction là chuyện không
            // nên xảy ra - nếu xảy ra thì có gì đó sai ở tầng dưới.
            _logger.LogError(
                "Không cập nhật được trạng thái: đơn {OrderId} không tồn tại.", orderId);

            throw new NotFoundException(nameof(Order), orderId);
        }

        if (!ChuyenTrangThaiHopLe(order.Status, status))
        {
            // Log TRƯỚC khi ném, kèm CẢ trạng thái hiện tại lẫn trạng thái muốn chuyển
            // sang. Chỉ nói "chuyển trạng thái không hợp lệ" là bắt người đọc log phải
            // mở DB ra mới hiểu chuyện gì đã xảy ra - mà lúc đó dữ liệu có thể đã đổi.
            _logger.LogError(
                "Chuyển trạng thái không hợp lệ cho đơn {OrderId}: {TrangThaiHienTai} -> {TrangThaiMoi}.",
                orderId, order.Status, status);

            // Nổ to thay vì âm thầm ghi đè. Một đơn từ Paid quay về Pending là mất dấu
            // một khoản tiền đã thu, và không có gì trong DB tố giác chuyện đó.
            throw new InvalidOrderStatusTransitionException(
                orderId, order.Status.ToString(), status.ToString());
        }

        order.Status = status;

        // ⚠ Câu chữ phải nói ĐÚNG những gì đã xảy ra: "đã đánh dấu", KHÔNG phải "đã
        // cập nhật thành công". Tại thời điểm này thay đổi mới nằm trong Change Tracker;
        // người gọi mới là bên quyết định có SaveChanges và commit hay không. Viết "đã
        // cập nhật" là tạo ra những dòng log khẳng định một việc mà transaction sau đó
        // có thể rollback - và khi đối soát thì log nói một đằng, DB nói một nẻo.
        _logger.LogInformation(
            "Đã đánh dấu đơn {OrderId} chuyển sang {TrangThaiMoi} (chờ người gọi lưu).",
            orderId, status);

        // CỐ Ý không gọi _unitOfWork.SaveChangesAsync() - xem chú thích ở IOrderService.
        // Người gọi giữ quyền quyết định ranh giới transaction, nhờ đó thay đổi này đi
        // chung MỘT lần ghi với bản ghi Payment.
    }

    /// <summary>
    /// Luật chuyển trạng thái, tập trung ở đúng một chỗ.
    ///
    /// <para>
    /// Hôm nay chỉ có một chuyển đổi hợp lệ: <c>Pending -&gt; Paid</c>. Viết dưới dạng
    /// bảng thay vì một câu <c>if</c> vì đây là thứ sẽ mọc thêm dòng (hoàn tiền, huỷ
    /// đơn), và lúc đó thêm một dòng dễ đọc hơn sửa một biểu thức boolean dài.
    /// </para>
    /// <para>
    /// <c>Paid -&gt; Paid</c> KHÔNG hợp lệ, dù nghe có vẻ vô hại: việc chịu được IPN
    /// gửi lại là trách nhiệm của <c>PaymentService</c> (nó trả mã 02 trước khi tới
    /// đây). Cho phép ở đây là che mất một lần gọi trùng thật sự.
    /// </para>
    /// </summary>
    private static bool ChuyenTrangThaiHopLe(OrderStatus tu, OrderStatus sang) =>
        (tu, sang) switch
        {
            (OrderStatus.Pending, OrderStatus.Paid) => true,
            _ => false
        };

    public Task<PagedResult<OrderSummary>> GetMyOrdersAsync(
        int userId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default) =>
        _orderRepository.GetPagedForUserAsync(userId, page, pageSize, cancellationToken);

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
            order.Status,

            // Đọc từ CỘT trên Orders, không join sang User: đơn hàng phải hiện đúng
            // thông tin đã khai lúc đặt, kể cả khi hồ sơ tài khoản đã đổi sau đó.
            new ShippingInfo(order.RecipientName, order.RecipientPhone, order.ShippingAddress),

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
