using System.Text.Json;
using MiniMart.Domain.Interfaces;
using MiniMart.Domain.ValueObjects;

namespace MiniMart.Web.Services;

/// <summary>
/// Giỏ hàng của KHÁCH VÃNG LAI, lưu trong ASP.NET Core Session.
///
/// <para>
/// Nằm ở tầng Web chứ không phải Infrastructure vì nó phụ thuộc
/// <c>HttpContext.Session</c> - khái niệm của tầng Web. Dự án đã có tiền lệ đúng
/// như vậy: <c>WebRootProductImageStorage</c> phụ thuộc <c>IWebHostEnvironment</c>
/// nên cũng đặt ở đây.
/// </para>
/// <para>
/// Hạn chế đã biết: Session mặc định nằm trong RAM của tiến trình. Restart server
/// là mất giỏ của mọi khách vãng lai, và chạy nhiều instance thì mỗi instance một
/// giỏ khác nhau. Cùng loại hạn chế với rate limiter - cần Redis khi scale ngang.
/// </para>
/// </summary>
public class SessionCartStore : ICartStore
{
    private const string SessionKey = "MiniMart.Cart";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public SessionCartStore(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ISession Session =>
        _httpContextAccessor.HttpContext?.Session
        ?? throw new InvalidOperationException(
            "Không truy cập được Session. Kiểm tra đã gọi builder.Services.AddSession() " +
            "và app.UseSession() (đứng SAU UseRouting, TRƯỚC phần map endpoint) chưa.");

    public async Task<IReadOnlyList<CartLine>> GetLinesAsync(
        CancellationToken cancellationToken = default)
    {
        return await DocAsync(cancellationToken);
    }

    public async Task SetQuantityAsync(
        int productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "Số lượng phải >= 1; muốn xoá thì gọi RemoveAsync.");
        }

        var lines = await DocAsync(cancellationToken);
        var conLai = lines.Where(l => l.ProductId != productId).ToList();

        // Ghi đè bằng cách loại dòng cũ rồi thêm dòng mới vào CUỐI: giữ nguyên
        // ý nghĩa "một sản phẩm một dòng" giống unique index bên DB.
        conLai.Add(new CartLine(productId, quantity));

        Ghi(conLai);
    }

    public async Task RemoveAsync(int productId, CancellationToken cancellationToken = default)
    {
        var lines = await DocAsync(cancellationToken);

        // Xoá thứ không có sẵn là thành công - giống DatabaseCartStore.
        Ghi(lines.Where(l => l.ProductId != productId).ToList());
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // LoadAsync trước khi Remove: thao tác trên session chưa nạp sẽ nạp đồng
        // bộ ngầm bên trong, tức sync-over-async - đúng thứ gây nghẽn thread pool.
        await Session.LoadAsync(cancellationToken);

        Session.Remove(SessionKey);
    }

    private async Task<List<CartLine>> DocAsync(CancellationToken cancellationToken)
    {
        await Session.LoadAsync(cancellationToken);

        var json = Session.GetString(SessionKey);

        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        // Session chỉ cất được string/int/byte[] nên giỏ phải đi qua JSON.
        // Dữ liệu này do CHÍNH server ghi ra (không phải input người dùng), nhưng
        // vẫn phòng trường hợp key bị ghi đè bởi phiên bản cũ có định dạng khác.
        try
        {
            return JsonSerializer.Deserialize<List<CartLine>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Ghi(List<CartLine> lines)
    {
        if (lines.Count == 0)
        {
            // Không cất chuỗi "[]": bỏ hẳn key giúp Session không phình ra vì
            // những giỏ rỗng của bot ghé qua rồi đi.
            Session.Remove(SessionKey);
            return;
        }

        Session.SetString(SessionKey, JsonSerializer.Serialize(lines));
    }
}
