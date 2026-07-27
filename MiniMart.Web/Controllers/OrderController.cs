using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniMart.Application.Interfaces;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Web.Controllers;

/// <summary>
/// "Đơn hàng của tôi" - danh sách và chi tiết đơn của chính người đang đăng nhập.
///
/// <para>
/// ★★ RÀNG BUỘC QUAN TRỌNG NHẤT CỦA FILE: <c>userId</c> LUÔN đến từ
/// <see cref="ICurrentUser"/>, tức từ cookie đã ký, và TUYỆT ĐỐI không bao giờ từ route
/// hay query string. Thêm một tham số <c>userId</c> vào bất kỳ action nào ở đây là mở
/// thẳng một lỗ IDOR: ai cũng đọc được đơn của người khác bằng cách đổi một con số trên
/// thanh địa chỉ.
/// </para>
/// <para>
/// An toàn ở đây đến từ <b>cấu trúc</b>, không từ một câu <c>if</c>: hai method của
/// Service đều nhận <c>userId</c> làm tham số bắt buộc và lọc NGAY TRONG truy vấn, nên
/// không tồn tại cách viết "quên kiểm tra chủ sở hữu". Cùng khuôn với việc giỏ hàng chỉ
/// nhận <c>productId</c>, xem <c>rules/cart.md</c>.
/// </para>
/// </summary>
[Authorize]
public class OrderController : Controller
{
    /// <summary>
    /// 10 đơn mỗi trang. Nhỏ hơn trang sản phẩm (12) vì mỗi dòng ở đây cao hơn nhiều
    /// và người xem thường chỉ tìm đơn gần nhất.
    /// </summary>
    private const int KichThuocTrang = 10;

    private readonly IOrderService _orderService;
    private readonly ICurrentUser _currentUser;

    public OrderController(IOrderService orderService, ICurrentUser currentUser)
    {
        _orderService = orderService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        // [Authorize] đã bảo đảm có người đăng nhập, nên Id null ở đây là LỖI LẬP TRÌNH
        // (nhiều khả năng cấu hình claim sai), không phải chuyện người dùng gây ra.
        // Ném để nó nổ to thay vì âm thầm trả về danh sách rỗng - "không có đơn nào"
        // trông y hệt "đã đăng nhập nhưng đọc nhầm claim".
        var userId = LayUserId();

        var trang = await _orderService.GetMyOrdersAsync(
            userId, page, KichThuocTrang, cancellationToken);

        // Server đã KẸP `page` (repository đưa -5 về 1), nên phải xoá khoá đó khỏi
        // ModelState. Không xoá thì mọi tag helper đọc `page` sẽ render lại giá trị THÔ
        // của người dùng - cùng cái bẫy đã bắt được ở bảng sửa hàng loạt.
        ModelState.Remove(nameof(page));

        return View(trang);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken = default)
    {
        var order = await _orderService.GetMyOrderAsync(id, LayUserId(), cancellationToken);

        // ★ 404 chứ KHÔNG 403, và đây là quyết định về rò rỉ thông tin.
        //
        // 403 ("bạn không có quyền") xác nhận rằng đơn số 42 CÓ TỒN TẠI - trong khi Id
        // đơn hàng tuần tự nên đoán được. Gộp "không có" và "không phải của bạn" thành
        // cùng một câu trả lời là cách duy nhất để không nói gì cả.
        //
        // Service đã trả null cho CẢ HAI trường hợp nên ở đây không phân biệt được kể
        // cả khi muốn - an toàn nhờ hình dạng của API, không nhờ kỷ luật của người viết.
        if (order is null)
        {
            return NotFound();
        }

        return View(order);
    }

    private int LayUserId() =>
        _currentUser.Id
        ?? throw new InvalidOperationException(
            "Không đọc được Id người dùng dù action đã có [Authorize]. "
            + "Nhiều khả năng claim ClaimTypes.NameIdentifier bị thiếu lúc đăng nhập.");
}
