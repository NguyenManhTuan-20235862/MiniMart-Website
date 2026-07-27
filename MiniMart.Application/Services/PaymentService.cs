using Microsoft.Extensions.Logging;
using MiniMart.Application.Interfaces;
using MiniMart.Application.Models;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Enums;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Application.Services;

/// <summary>
/// Ghi nhận thanh toán từ kênh IPN của VNPay.
///
/// <para>
/// Bốn lệnh kiểm, và THỨ TỰ của chúng là một phần của hợp đồng - xem chú thích trong
/// <see cref="XuLyIpnAsync"/>.
/// </para>
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IVnPayService _vnPayService;
    private readonly IOrderService _orderService;
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentService> _logger;

    /// <param name="orderService">
    /// Service-to-service, và không tạo phụ thuộc vòng: <c>OrderService</c> không biết
    /// <c>PaymentService</c> tồn tại. Chiều phụ thuộc chỉ đi một hướng.
    /// </param>
    public PaymentService(
        IVnPayService vnPayService,
        IOrderService orderService,
        IOrderRepository orderRepository,
        IPaymentRepository paymentRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<PaymentService> logger)
    {
        _vnPayService = vnPayService;
        _orderService = orderService;
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<string> TaoUrlThanhToanAsync(
        int orderId,
        int userId,
        string clientIpAddress,
        CancellationToken cancellationToken = default)
    {
        // Lọc theo userId NGAY TRONG truy vấn, không đọc lên rồi so sánh sau.
        var order = await _orderRepository.GetByIdForUserAsync(orderId, userId, cancellationToken);

        if (order is null)
        {
            throw new NotFoundException(nameof(Order), orderId);
        }

        if (order.Status != OrderStatus.Pending)
        {
            // Dựng lệnh thanh toán cho một đơn đã trả là mời khách trả lần thứ hai.
            throw new InvalidOperationException(
                $"Đơn hàng {orderId} đang ở trạng thái {order.Status}, không thể tạo lệnh thanh toán.");
        }

        // Số tiền đi vào URL là TotalAmount ĐÃ CHỐT trong đơn - đây chính là con số mà
        // kênh IPN sẽ đối chiếu lại khi VNPay báo về. Lấy từ bất kỳ nguồn nào khác (giỏ
        // hàng, form) là tự tạo ra khoảng cách giữa số tiền thu và số tiền của đơn.
        return _vnPayService.CreatePaymentUrl(order, clientIpAddress);
    }

    public async Task<IpnResult> XuLyIpnAsync(
        IReadOnlyDictionary<string, string?> thamSo,
        CancellationToken cancellationToken = default)
    {
        // ───── KIỂM 1: chữ ký ─────
        //
        // Luôn đứng đầu. Trước khi bước này xong, MỌI trường trong thamSo chỉ là chuỗi
        // do người gửi tự đặt - kể cả vnp_TxnRef và vnp_Amount. Truy vấn DB bằng một
        // giá trị chưa xác thực là để người lạ điều khiển câu truy vấn của ta.
        var ketQua = _vnPayService.Verify(thamSo);

        if (!ketQua.ChuKyHopLe)
        {
            // Warning, KHÔNG Error: dữ liệu đến từ internet nên chữ ký sai là chuyện
            // thường ngày (bot quét URL). Ghi Error là làm ngập log tới mức sự cố thật
            // bị chìm - cùng lý do với việc PaymentController không ném exception ở đây.
            //
            // KHÔNG log giá trị các tham số: tại thời điểm này chưa có gì được xác thực,
            // nên mọi trường đều là chuỗi do người gửi tự đặt. Ghi chúng vào log là để
            // người lạ bơm nội dung tuỳ ý vào file log của ta (log injection/forging).
            _logger.LogWarning("IPN bị từ chối: chữ ký không hợp lệ.");

            return IpnResult.SaiChuKy;
        }

        if (ketQua.OrderId is not int orderId)
        {
            _logger.LogWarning("IPN: chữ ký hợp lệ nhưng vnp_TxnRef không phải OrderId hợp lệ.");

            return IpnResult.KhongTimThayDon;
        }

        // Chỉ TỪ ĐÂY trở đi mới được ghi giá trị các tham số vào log: chữ ký đã xác
        // nhận rằng chúng do VNPay tạo ra. Đây cũng là dòng đánh dấu "một IPN thật đã
        // tới" - thứ cần có khi khách khiếu nại "tôi trả rồi mà đơn vẫn chưa thanh toán",
        // vì nó phân biệt được "VNPay chưa từng gọi" với "gọi rồi nhưng ta xử lý sai".
        _logger.LogInformation(
            "IPN hợp lệ cho đơn {OrderId}: mã phản hồi {ResponseCode}, số tiền {Amount}, giao dịch {TransactionNo}.",
            orderId, ketQua.ResponseCode, ketQua.Amount, ketQua.TransactionNo);

        try
        {
            // Transaction mở TRƯỚC lần đọc đơn, không phải trước lần ghi - cùng lý do
            // với CheckoutAsync: lệnh đối chiếu số tiền là một phép ĐỌC quyết định một
            // phép GHI, nên hai thứ đó phải nhìn cùng một snapshot dữ liệu.
            //
            // await using: không commit thì DisposeAsync rollback, nên mọi đường thoát
            // sớm ở dưới (04, 01, 02) đều tự dọn dẹp mà không cần try/catch nào.
            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            // ───── KIỂM 2: đơn có tồn tại không ─────
            //
            // GetForUpdateAsync (CÓ tracking) vì đường này sẽ GHI. Không lọc theo
            // userId - và đó là đúng, không phải lỗ IDOR: request này đến từ máy chủ
            // VNPay, không có người dùng đăng nhập nào. Thứ xác thực nó là CHỮ KÝ đã
            // kiểm ở trên, không phải cookie.
            var order = await _orderRepository.GetForUpdateAsync(orderId, cancellationToken);

            if (order is null)
            {
                _logger.LogWarning("IPN: không tìm thấy đơn {OrderId}.", orderId);

                return IpnResult.KhongTimThayDon;
            }

            // ───── KIỂM 3: SỐ TIỀN ─────
            //
            // ★★ Lệnh kiểm quan trọng nhất của cả hệ thống thanh toán. Chữ ký hợp lệ
            // CHỈ chứng minh "thông báo này do VNPay tạo ra" - nó KHÔNG chứng minh
            // "số tiền này đúng với đơn hàng của ta". Hai câu đó khác nhau hoàn toàn,
            // và khoảng cách giữa chúng chính là chỗ mất tiền.
            //
            // Kịch bản cụ thể: nếu có bất kỳ đường nào khiến số tiền lúc TẠO lệnh
            // thanh toán khác số tiền của đơn (một tham số lọt vào từ form, một lỗi
            // làm tròn, một lần sửa đơn sau khi khách đã bấm thanh toán), thì VNPay
            // sẽ thu đúng số tiền nhỏ đó, ký một thông báo HOÀN TOÀN HỢP LỆ, và IPN
            // tới đây báo "thành công". Không có lệnh kiểm này thì đơn 10 triệu được
            // đánh dấu đã thanh toán bằng 10 nghìn - không exception, không log lỗi,
            // không gì cả.
            //
            // Nói cách khác: chữ ký bảo vệ TÍNH TOÀN VẸN của thông điệp, còn lệnh kiểm
            // này bảo vệ TÍNH ĐÚNG ĐẮN của giao dịch. Không cái nào thay được cái kia.
            //
            // So sánh với TotalAmount đã LƯU trong DB, không phải với tổng tính lại từ
            // giỏ hàng: giỏ có thể đã đổi, còn con số ràng buộc với khách là con số
            // trong đơn.
            if (ketQua.Amount != order.TotalAmount)
            {
                // Log ở mức Warning kèm CẢ HAI con số: đây là dấu hiệu hoặc có bug ở
                // đường tạo lệnh thanh toán, hoặc có người đang thử. Cả hai đều cần
                // người đọc log nhìn thấy ngay.
                _logger.LogWarning(
                    "IPN: số tiền không khớp cho đơn {OrderId}. Cổng báo {AmountCong}, đơn là {AmountDon}.",
                    orderId, ketQua.Amount, order.TotalAmount);

                return IpnResult.SaiSoTien;
            }

            // ───── KIỂM 4: đã ghi nhận chưa ─────
            //
            // VNPay gửi lại IPN khi chưa nhận được phản hồi, nên endpoint này BẮT BUỘC
            // idempotent. Đây là lệnh kiểm cho "đường đẹp"; bảo đảm thật là
            // UNIQUE(Payments.OrderId) ở dưới - giữa lệnh kiểm này và SaveChanges vẫn
            // còn khe TOCTOU, y hệt mọi lệnh kiểm nghiệp vụ khác trong dự án.
            if (order.Status != OrderStatus.Pending)
            {
                _logger.LogInformation(
                    "IPN: đơn {OrderId} đã ở trạng thái {TrangThai}, bỏ qua.",
                    orderId, order.Status);

                return IpnResult.DonDaXacNhan;
            }

            var thanhCong = ketQua.ThanhToanThanhCong;

            await _paymentRepository.AddAsync(
                new Payment
                {
                    OrderId = order.Id,
                    Status = thanhCong ? PaymentStatus.Succeeded : PaymentStatus.Failed,

                    // Lưu số tiền CỔNG BÁO, không chép lại order.TotalAmount. Ở đây
                    // hai số bằng nhau (vừa kiểm xong) nhưng đây là bản ghi của bên
                    // kia - giá trị của nó nằm ở chỗ nó độc lập khi đối soát.
                    Amount = ketQua.Amount ?? 0m,

                    TransactionNo = ketQua.TransactionNo ?? string.Empty,
                    BankCode = ketQua.BankCode ?? string.Empty,
                    ResponseCode = ketQua.ResponseCode ?? string.Empty,
                    CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
                },
                cancellationToken);

            if (thanhCong)
            {
                // Uỷ cho OrderService thay vì tự gán order.Status = Paid.
                //
                // Lý do không phải hình thức: OrderService là nơi duy nhất giữ luật
                // chuyển trạng thái (Pending -> Paid mới hợp lệ). Gán thẳng ở đây là
                // tạo đường thứ hai ghi vào cột đó, và luật kia thành thứ chỉ đúng khi
                // người viết nhớ tới nó.
                //
                // Method đó CỐ Ý không tự SaveChanges, nhờ vậy thay đổi này vẫn nằm
                // chung một lần ghi với INSERT Payment ở trên.
                await _orderService.UpdatePaymentStatusAsync(
                    order.Id, OrderStatus.Paid, cancellationToken);
            }

            // MỘT SaveChanges cho cả hai thay đổi (INSERT Payment + UPDATE Order).
            //
            // ★ Đây mới là thứ tạo ra tính nguyên tử hôm nay, KHÔNG phải transaction
            // tường minh ở trên: EF Core đã tự bọc mỗi SaveChanges trong một transaction
            // ngầm. Đã đo bằng mutation ở Phase 5 - bỏ transaction tường minh mà giữ
            // một SaveChanges thì mọi test vẫn xanh.
            //
            // Transaction tường minh vẫn được giữ vì nó là LƯỚI AN TOÀN: nó biến một
            // refactor sai kinh điển - "tách ra lưu từng thứ cho chắc" - từ bug dữ liệu
            // thành chuyện không xảy ra. Ghi Payment thành công mà Order chưa Paid là
            // trạng thái không ai phát hiện được, vì cả hai đều "có vẻ" đúng.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            // ★ Log SAU CommitAsync. Trước commit thì câu này có thể là nói dối -
            // SaveChanges đã chạy nhưng transaction vẫn còn có thể rollback, và lúc đó
            // log khẳng định đơn đã Paid trong khi DB nói ngược lại. Với một bản ghi tài
            // chính thì khoảng cách đó là thứ sẽ tốn hàng giờ khi đối soát.
            //
            // Ghi CẢ trường hợp giao dịch thất bại (thanhCong = false) chứ không chỉ khi
            // thành công: bản ghi đó chính là câu trả lời khi khách gọi lên nói "tôi trả
            // rồi". Phân biệt bằng mức log - Information cho thành công, Warning cho
            // thất bại - để lọc nhanh mà không phải đọc từng dòng.
            if (thanhCong)
            {
                _logger.LogInformation(
                    "Đơn {OrderId} đã chuyển sang Paid theo IPN, giao dịch {TransactionNo}, số tiền {Amount}.",
                    orderId, ketQua.TransactionNo, ketQua.Amount);
            }
            else
            {
                _logger.LogWarning(
                    "IPN báo giao dịch THẤT BẠI cho đơn {OrderId}: mã {ResponseCode}. Đơn giữ nguyên trạng thái {TrangThai}.",
                    orderId, ketQua.ResponseCode, order.Status);
            }

            return IpnResult.ThanhCong;
        }
        catch (DuplicateKeyException)
        {
            // UNIQUE(Payments.OrderId) đã chặn. Nghĩa là một IPN khác vừa ghi xong
            // TRONG khe TOCTOU của lệnh kiểm 4 - đúng chuyện endpoint idempotent phải
            // chịu được, không phải sự cố.
            _logger.LogInformation("IPN trùng cho đơn {OrderId} - đã có bản ghi thanh toán.", orderId);

            return IpnResult.DonDaXacNhan;
        }
        catch (Exception ex)
        {
            // Bắt hết là CHỦ Ý ở đúng chỗ này, dù trái với thói quen chung.
            //
            // Để exception lọt lên Controller là HTTP 500, mà VNPay đọc 500 là "chưa
            // nhận được" và sẽ gửi lại. Với lỗi tạm thời thì tốt; với lỗi cố định (một
            // NullReferenceException chẳng hạn) thì nó gửi lại vô hạn. Trả 99 tường
            // minh vẫn cho VNPay thử lại nhưng giữ quyền kiểm soát ở phía ta.
            //
            // Đổi lại, BẮT BUỘC log kèm exception - nuốt lỗi mà không log là biến một
            // sự cố thành sự im lặng.
            _logger.LogError(ex, "IPN: lỗi ngoài dự kiến khi xử lý đơn {OrderId}.", orderId);

            return IpnResult.LoiKhac;
        }
    }
}
