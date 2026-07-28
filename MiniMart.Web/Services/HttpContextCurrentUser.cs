using System.Globalization;
using System.Security.Claims;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Web.Services;

/// <summary>
/// Đọc danh tính từ <c>HttpContext.User</c> - tức là từ COOKIE, không phải từ DB.
///
/// <para>
/// Hệ quả cần nhớ: claims là ảnh chụp lúc đăng nhập. Xoá tài khoản dưới DB thì
/// <see cref="Id"/> ở đây vẫn trả về id cũ cho tới khi cookie hết hạn - lúc đó
/// thao tác giỏ hàng sẽ đổ ở khoá ngoại Carts -> Users. Chấp nhận được vì xoá
/// tài khoản là việc hiếm.
/// </para>
/// </summary>
public class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public int? Id
    {
        get
        {
            // Đúng ClaimTypes.NameIdentifier - cùng hằng mà AccountController dùng
            // lúc tạo claims. Gõ chuỗi tự chế ở một trong hai nơi thì giá trị luôn
            // null và giỏ hàng im lặng không hoạt động.
            var giaTri = User?.FindFirstValue(ClaimTypes.NameIdentifier);

            return int.TryParse(giaTri, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;
        }
    }
}
