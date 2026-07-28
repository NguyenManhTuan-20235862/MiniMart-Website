using Microsoft.Extensions.Logging.Abstractions;
using MiniMart.Application.Interfaces;
using MiniMart.Application.Services;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Enums;
using MiniMart.Domain.Interfaces;
using MiniMart.Domain.ValueObjects;
using Moq;

namespace MiniMart.Tests;

/// <summary>
/// Nghiệp vụ IPN - unit test với Moq, không cần DB.
///
/// <para>
/// Bốn lệnh kiểm (chữ ký, đơn tồn tại, số tiền, đã ghi nhận chưa) và THỨ TỰ của chúng
/// đều kiểm được ở đây. Đó là lý do toàn bộ nghiệp vụ nằm ở Application chứ không ở
/// Controller: nhét vào Controller là biến những thứ này thành chỉ-kiểm-được-bằng-HTTP.
/// </para>
/// </summary>
public class PaymentServiceTests
{
    private const int OrderId = 12345;
    private const decimal TongTien = 1_250_000m;

    private readonly Mock<IVnPayService> _vnPayService = new();
    private readonly Mock<IOrderService> _orderService = new();
    private readonly Mock<IOrderRepository> _orderRepository = new();
    private readonly Mock<IPaymentRepository> _paymentRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ITransaction> _transaction = new();

    /// <summary>Ghi lại thứ tự các bước để khẳng định Commit đứng SAU SaveChanges.</summary>
    private readonly List<string> _thuTu = [];

    private Payment? _paymentDaLuu;
    private int _soLanLuu;

    public PaymentServiceTests()
    {
        _unitOfWork
            .Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_transaction.Object)
            .Callback(() => _thuTu.Add("begin"));

        _transaction
            .Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => _thuTu.Add("commit"));

        _transaction.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _paymentRepository
            .Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((p, _) => _paymentDaLuu = p)
            .Returns(Task.CompletedTask);

        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1)
            .Callback(() => { _soLanLuu++; _thuTu.Add("save"); });
    }

    // ───────────── Đường thành công ─────────────

    [Fact]
    public async Task Chu_ky_dung_so_tien_dung_ma_00_thi_don_thanh_Paid()
    {
        var service = TaoService(TaoDon(), TaoKetQua());

        var ketQua = await service.XuLyIpnAsync(ThamSo());

        Assert.Equal("00", ketQua.RspCode);
        Assert.Equal(PaymentStatus.Succeeded, _paymentDaLuu!.Status);
        Assert.Equal(1, _soLanLuu);

        // Uỷ cho OrderService thay vì tự gán order.Status: đó là nơi duy nhất giữ luật
        // chuyển trạng thái. Ở unit test này IOrderService là mock nên kiểm TƯƠNG TÁC;
        // việc trạng thái thật sự đổi dưới DB do PaymentIpnTests khoá.
        _orderService.Verify(
            s => s.UpdatePaymentStatusAsync(OrderId, OrderStatus.Paid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Doi_trang_thai_va_ghi_Payment_nam_trong_CUNG_mot_transaction()
    {
        var service = TaoService(TaoDon(), TaoKetQua());

        await service.XuLyIpnAsync(ThamSo());

        // Commit phải đứng SAU SaveChanges: commit trước là commit một transaction chưa
        // có gì trong đó, và thay đổi sau đó nằm ngoài mọi transaction.
        Assert.Equal(new[] { "begin", "save", "commit" }, _thuTu.ToArray());
    }

    [Fact]
    public async Task Duong_thoat_som_KHONG_commit()
    {
        var service = TaoService(TaoDon(), TaoKetQua(amount: 1m));

        await service.XuLyIpnAsync(ThamSo());

        // Số tiền sai -> thoát ở giữa. `await using` lo phần rollback, nhưng điều phải
        // khẳng định là KHÔNG có commit nào - commit một transaction rỗng thì vô hại,
        // commit một transaction có thay đổi dở dang thì không.
        Assert.DoesNotContain("commit", _thuTu);
        Assert.DoesNotContain("save", _thuTu);
    }

    [Fact]
    public async Task Ban_ghi_thanh_toan_luu_du_thong_tin_de_doi_soat()
    {
        var service = TaoService(TaoDon(), TaoKetQua());

        await service.XuLyIpnAsync(ThamSo());

        // Mã giao dịch VNPay là thứ DUY NHẤT tra cứu được khi khách khiếu nại.
        Assert.Equal(OrderId, _paymentDaLuu!.OrderId);
        Assert.Equal(TongTien, _paymentDaLuu.Amount);
        Assert.Equal("14200000", _paymentDaLuu.TransactionNo);
        Assert.Equal("NCB", _paymentDaLuu.BankCode);
        Assert.Equal("00", _paymentDaLuu.ResponseCode);
    }

    // ───────────── ★ Đối chiếu số tiền ─────────────

    [Theory]
    [InlineData(1)]                 // trả 1 đồng cho đơn 1.250.000
    [InlineData(1_249_999)]         // thiếu đúng 1 đồng
    [InlineData(1_250_001)]         // thừa
    [InlineData(125_000_000)]       // quên chia 100
    public async Task So_tien_khong_khop_thi_TU_CHOI_va_don_KHONG_thanh_Paid(decimal soTienCong)
    {
        var order = TaoDon();
        var service = TaoService(order, TaoKetQua(amount: soTienCong));

        var ketQua = await service.XuLyIpnAsync(ThamSo());

        // ★★ Lệnh kiểm quan trọng nhất của cả hệ thống thanh toán.
        //
        // Chú ý: chữ ký ở đây HỢP LỆ - mock Verify trả về ChuKyHopLe = true. Nghĩa là
        // thông báo này THẬT SỰ do VNPay tạo ra. Chữ ký hợp lệ mà số tiền vẫn sai là
        // hoàn toàn có thể, và đó chính là lý do lệnh kiểm này không thể bỏ.
        Assert.Equal("04", ketQua.RspCode);
        Assert.Equal(OrderStatus.Pending, order.Status);

        _orderService.Verify(
            s => s.UpdatePaymentStatusAsync(
                It.IsAny<int>(), It.IsAny<OrderStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Không ghi gì cả - kể cả một bản ghi Failed.
        Assert.Null(_paymentDaLuu);
        Assert.Equal(0, _soLanLuu);
    }

    [Fact]
    public async Task So_tien_doi_chieu_voi_TotalAmount_da_luu()
    {
        // Đơn có tổng 999.999 nhưng cổng báo 1.250.000 (số của một đơn khác).
        var order = TaoDon(tongTien: 999_999m);
        var service = TaoService(order, TaoKetQua(amount: TongTien));

        var ketQua = await service.XuLyIpnAsync(ThamSo());

        // So với con số ĐÃ CHỐT trong đơn, không phải tổng tính lại từ giỏ hàng: giỏ
        // có thể đã đổi, còn con số ràng buộc với khách là con số trong đơn.
        Assert.Equal("04", ketQua.RspCode);
    }

    [Fact]
    public async Task Kiem_so_tien_chay_TRUOC_khi_ghi_bat_ky_thu_gi()
    {
        var service = TaoService(TaoDon(), TaoKetQua(amount: 1m));

        await service.XuLyIpnAsync(ThamSo());

        // Thứ tự là một phần của hợp đồng: ghi Payment rồi mới kiểm tiền thì đã có
        // một bản ghi tài chính sai nằm trong DB, và UNIQUE(OrderId) khiến IPN đúng
        // gửi lại sau đó KHÔNG ghi được nữa.
        _paymentRepository.Verify(
            r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ───────────── Chữ ký ─────────────

    [Fact]
    public async Task Chu_ky_sai_thi_tra_97_va_KHONG_cham_DB()
    {
        var service = TaoService(TaoDon(), VnPayReturn.KhongHopLe);

        var ketQua = await service.XuLyIpnAsync(ThamSo());

        Assert.Equal("97", ketQua.RspCode);

        // Trước khi chữ ký được xác nhận, vnp_TxnRef chỉ là chuỗi do người gửi tự đặt.
        // Truy vấn DB bằng nó là để người lạ điều khiển câu truy vấn của ta.
        _orderRepository.Verify(
            r => r.GetForUpdateAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ───────────── Đơn không tồn tại ─────────────

    [Fact]
    public async Task Khong_tim_thay_don_thi_tra_01()
    {
        var service = TaoService(order: null, TaoKetQua());

        var ketQua = await service.XuLyIpnAsync(ThamSo());

        Assert.Equal("01", ketQua.RspCode);
        Assert.Null(_paymentDaLuu);
    }

    [Fact]
    public async Task TxnRef_khong_phai_so_thi_tra_01()
    {
        var service = TaoService(TaoDon(), TaoKetQua() with { OrderId = null });

        var ketQua = await service.XuLyIpnAsync(ThamSo());

        Assert.Equal("01", ketQua.RspCode);
    }

    // ───────────── Idempotent ─────────────

    [Fact]
    public async Task Don_da_Paid_thi_tra_02_va_KHONG_ghi_them()
    {
        var order = TaoDon();
        order.Status = OrderStatus.Paid;

        var service = TaoService(order, TaoKetQua());

        var ketQua = await service.XuLyIpnAsync(ThamSo());

        // VNPay gửi lại IPN khi chưa nhận được phản hồi. Ghi thêm một bản ghi thanh
        // toán nữa là nhân đôi doanh thu trong mọi báo cáo.
        Assert.Equal("02", ketQua.RspCode);
        Assert.Null(_paymentDaLuu);
        Assert.Equal(0, _soLanLuu);
    }

    [Fact]
    public async Task Hai_IPN_song_song_thi_cai_thua_nhan_02_chu_khong_phai_500()
    {
        var service = TaoService(TaoDon(), TaoKetQua());

        // Lệnh kiểm "đã Paid chưa" có khe TOCTOU: hai IPN song song đều đọc thấy
        // Pending. UNIQUE(Payments.OrderId) là bảo đảm cuối, và UnitOfWork dịch lỗi
        // 2601/2627 của SQL Server thành DuplicateKeyException.
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateKeyException(
                new InvalidOperationException("UX_Payments_OrderId")));

        var ketQua = await service.XuLyIpnAsync(ThamSo());

        // 02 chứ không phải 99: đây là chuyện endpoint idempotent PHẢI chịu được,
        // không phải sự cố. Trả 99 sẽ khiến VNPay gửi lại mãi.
        Assert.Equal("02", ketQua.RspCode);
    }

    // ───────────── Giao dịch thất bại vẫn phải ghi nhận ─────────────

    [Theory]
    [InlineData("24", "02")]   // khách tự huỷ
    [InlineData("51", "02")]   // không đủ số dư
    [InlineData("00", "02")]   // cổng OK nhưng giao dịch không thành
    public async Task Giao_dich_that_bai_van_tra_00_va_van_ghi_ban_ghi_Failed(
        string responseCode, string transactionStatus)
    {
        var order = TaoDon();
        var service = TaoService(order, TaoKetQua(responseCode, transactionStatus));

        var ketQua = await service.XuLyIpnAsync(ThamSo());

        // ★ Điểm rất dễ nhầm: RspCode trả lời "tôi đã NHẬN và xử lý xong thông báo
        // của bạn chưa", KHÔNG phải "giao dịch có thành công không". Trả mã lỗi cho
        // một giao dịch thất bại sẽ khiến VNPay tưởng ta chưa nhận được và gửi lại mãi.
        Assert.Equal("00", ketQua.RspCode);

        // Đơn KHÔNG được thành Paid...
        Assert.Equal(OrderStatus.Pending, order.Status);

        // ...nhưng vẫn ghi lại, kèm mã lỗi. Khi khách gọi lên nói "tôi trả rồi", câu
        // trả lời nằm ở chính bản ghi này.
        Assert.Equal(PaymentStatus.Failed, _paymentDaLuu!.Status);
        Assert.Equal(responseCode, _paymentDaLuu.ResponseCode);
    }

    // ───────────── Không bao giờ ném ─────────────

    [Fact]
    public async Task Loi_ngoai_du_kien_thi_tra_99_chu_khong_nem()
    {
        var service = TaoService(TaoDon(), TaoKetQua());

        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hong that"));

        // Exception lọt lên Controller thành HTTP 500, mà VNPay đọc 500 là "chưa nhận
        // được" và gửi lại - với lỗi cố định thì gửi lại vô hạn.
        var ketQua = await service.XuLyIpnAsync(ThamSo());

        Assert.Equal("99", ketQua.RspCode);
    }

    // ───────────── Helper ─────────────

    private static Order TaoDon(decimal? tongTien = null) => new()
    {
        Id = OrderId,
        TotalAmount = tongTien ?? TongTien,
        Status = OrderStatus.Pending
    };

    private static VnPayReturn TaoKetQua(
        string responseCode = "00",
        string transactionStatus = "00",
        decimal? amount = null) =>
        new(
            ChuKyHopLe: true,
            OrderId: OrderId,
            ResponseCode: responseCode,
            TransactionStatus: transactionStatus,
            TransactionNo: "14200000",
            BankCode: "NCB",
            Amount: amount ?? TongTien);

    private static Dictionary<string, string?> ThamSo() =>
        new(StringComparer.Ordinal) { ["vnp_TxnRef"] = OrderId.ToString() };

    private PaymentService TaoService(Order? order, VnPayReturn ketQuaVerify)
    {
        _vnPayService
            .Setup(s => s.Verify(It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .Returns(ketQuaVerify);

        _orderRepository
            .Setup(r => r.GetForUpdateAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        return new PaymentService(
            _vnPayService.Object,
            _orderService.Object,
            _orderRepository.Object,
            _paymentRepository.Object,
            _unitOfWork.Object,
            TimeProvider.System,
            NullLogger<PaymentService>.Instance);
    }
}
