namespace MiniMart.Domain.Interfaces;

/// <summary>
/// Người dùng đang thực hiện request hiện tại.
///
/// <para>
/// Tồn tại vì <c>DatabaseCartStore</c> (Infrastructure) phải biết giỏ hàng
/// thuộc về ai, mà danh tính lại nằm trong <c>HttpContext.User</c> - khái niệm
/// của tầng Web. Không có abstraction này thì Infrastructure phải tham chiếu
/// ASP.NET Core, tức phá luôn chiều phụ thuộc của kiến trúc.
/// </para>
/// <para>
/// Đây cũng chính là DIP: Domain khai báo thứ nó CẦN ("cho tôi biết ai đang
/// thao tác"), tầng Web cung cấp thứ đó.
/// </para>
/// </summary>
public interface ICurrentUser
{
    /// <summary>Id người dùng, null khi là khách vãng lai.</summary>
    int? Id { get; }

    bool IsAuthenticated { get; }
}
