using Microsoft.EntityFrameworkCore;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Domain.ValueObjects;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Infrastructure.Repositories;

/// <summary>
/// Giỏ hàng của người ĐÃ ĐĂNG NHẬP, lưu ở bảng Carts/CartItems.
///
/// <para>
/// Chỉ đánh dấu thay đổi trong Change Tracker, KHÔNG gọi SaveChanges - đó là
/// việc của <c>IUnitOfWork</c> do Service điều khiển.
/// </para>
/// </summary>
public class DatabaseCartStore : ICartStore
{
    private readonly MiniMartDbContext _context;
    private readonly ICurrentUser _currentUser;

    public DatabaseCartStore(MiniMartDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Ném nếu bị dùng cho khách vãng lai. Đây là lỗi LẬP TRÌNH (factory ở
    /// Program.cs chọn nhầm kho), không phải lỗi người dùng, nên phải nổ to
    /// ngay thay vì âm thầm thao tác trên giỏ của userId = 0.
    /// </summary>
    private int UserId => _currentUser.Id
        ?? throw new InvalidOperationException(
            "DatabaseCartStore chỉ dùng cho người đã đăng nhập. " +
            "Khách vãng lai phải được factory định tuyến sang SessionCartStore.");

    public async Task<IReadOnlyList<CartLine>> GetLinesAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = UserId;

        // Đường ĐỌC -> AsNoTracking. Không Include(Product): CartLine cố ý chỉ
        // mang ProductId, việc nạp tên/giá là của CartService.
        return await _context.CartItems
            .AsNoTracking()
            .Where(i => i.Cart.UserId == userId)
            .OrderBy(i => i.Id)
            .Select(i => new CartLine(i.ProductId, i.Quantity))
            .ToListAsync(cancellationToken);
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

        var cart = await LayHoacTaoGioAsync(cancellationToken);

        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);

        if (item is null)
        {
            cart.Items.Add(new CartItem { ProductId = productId, Quantity = quantity });
        }
        else
        {
            item.Quantity = quantity;
        }

        cart.UpdatedAt = DateTime.UtcNow;
    }

    public async Task RemoveAsync(int productId, CancellationToken cancellationToken = default)
    {
        var cart = await LayGioAsync(cancellationToken);

        var item = cart?.Items.FirstOrDefault(i => i.ProductId == productId);

        if (item is null)
        {
            // Xoá thứ không có sẵn là thành công - kết quả mong muốn đã đạt.
            // Ném ở đây sẽ khiến bấm nút xoá hai lần bị báo lỗi vô cớ.
            return;
        }

        cart!.Items.Remove(item);
        _context.CartItems.Remove(item);
        cart.UpdatedAt = DateTime.UtcNow;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var cart = await LayGioAsync(cancellationToken);

        if (cart is null)
        {
            return;
        }

        _context.CartItems.RemoveRange(cart.Items);
        cart.Items.Clear();
        cart.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Bản CÓ tracking kèm Items - dùng cho mọi đường ghi.
    ///
    /// <para>
    /// Hỏi Change Tracker TRƯỚC, chỉ chạm DB khi chưa có. Bỏ bước này là bug thật
    /// đã bị test hợp đồng bắt được: hai lần <see cref="SetQuantityAsync"/> liên
    /// tiếp trước một SaveChanges thì lần thứ hai truy vấn lại DB, và
    /// <c>Include(c =&gt; c.Items)</c> nạp lại collection từ kết quả truy vấn -
    /// mà DB chưa có dòng nào - nên dòng đang chờ lưu của lần thứ nhất BIẾN MẤT.
    /// </para>
    /// <para>
    /// Local cũng chứa entity ở trạng thái Added, nhờ vậy tránh luôn việc tạo
    /// giỏ thứ hai cho cùng một người khi giỏ đầu chưa kịp lưu.
    /// </para>
    /// </summary>
    private async Task<Cart?> LayGioAsync(CancellationToken cancellationToken)
    {
        var userId = UserId;

        var dangTheoDoi = _context.Carts.Local.FirstOrDefault(c => c.UserId == userId);

        if (dangTheoDoi is not null)
        {
            return dangTheoDoi;
        }

        return await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
    }

    /// <remarks>
    /// Có khe race: hai request đồng thời của cùng một người đều thấy "chưa có
    /// giỏ" và cùng tạo. Unique index trên Carts.UserId sẽ chặn cái thứ hai và
    /// UnitOfWork dịch thành DuplicateKeyException - đúng tinh thần "validate ở
    /// Service, ràng buộc ở DB". Cách xử lý (thử lại một lần) thuộc về
    /// CartService ở bước 4, không phải ở đây.
    /// </remarks>
    private async Task<Cart> LayHoacTaoGioAsync(CancellationToken cancellationToken)
    {
        var cart = await LayGioAsync(cancellationToken);

        if (cart is not null)
        {
            return cart;
        }

        var moi = new Cart
        {
            UserId = UserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Carts.AddAsync(moi, cancellationToken);

        return moi;
    }
}
