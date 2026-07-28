using Microsoft.EntityFrameworkCore;
using MiniMart.Common;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Domain.ValueObjects;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly MiniMartDbContext _context;

    public OrderRepository(MiniMartDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Chỉ Add order: EF Core tự nhận ra các OrderDetail trong order.Items là
        // entity mới và insert kèm, gán OrderId sau khi Order có Id thật.
        await _context.Orders.AddAsync(order, cancellationToken);
    }

    public async Task<Order?> GetForUpdateAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        // CÓ tracking (không AsNoTracking): đường ghi. Entity đọc bằng AsNoTracking
        // sửa xong gọi SaveChanges sẽ không lưu gì và không có lỗi nào báo.
        //
        // Không Include(Items): người gọi duy nhất là IPN, nó chỉ đọc TotalAmount và
        // ghi Status. Nạp thừa các dòng đơn là một lần JOIN không ai dùng tới.
        return await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    public async Task<PagedResult<OrderSummary>> GetPagedForUserAsync(
        int userId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        // Kẹp tham số đến từ query string, y như GetProductsAsync: ?page=-5 và
        // ?pageSize=999999 không được phép chạm tới OFFSET/FETCH.
        if (page < 1)
        {
            page = 1;
        }

        pageSize = Math.Clamp(pageSize, 1, 100);

        // Điều kiện UserId nằm TRONG truy vấn. Đọc tất cả rồi lọc trong bộ nhớ cũng
        // ra đúng kết quả, nhưng nó kéo đơn của mọi người về tiến trình của ta - và
        // chỉ cần một lần quên `Where` là rò rỉ toàn bộ.
        var query = _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(o => o.CreatedAt)

            // ★ Tie-breaker BẮT BUỘC, và ở đây nó dễ vướng hơn ở trang sản phẩm: hai
            // đơn đặt trong cùng một giây có CreatedAt bằng nhau (DateTime2 vẫn có thể
            // trùng khi seed hoặc khi đặt nhanh), lúc đó SQL Server được tự do sắp xếp
            // khác nhau giữa hai lần chạy - một đơn hiện ở cả trang 1 lẫn trang 2 trong
            // khi đơn khác biến mất.
            //
            // Giảm dần theo Id để cùng chiều với CreatedAt: đơn mới hơn luôn có Id lớn hơn.
            .ThenByDescending(o => o.Id)

            .Skip((page - 1) * pageSize)
            .Take(pageSize)

            // ★ CHIẾU (project) ngay trong truy vấn, KHÔNG Include rồi map trong bộ nhớ.
            //
            // EF Core dịch `o.Items.Sum(...)` thành một subquery SUM chạy dưới DB, nên
            // không một dòng OrderDetail nào rời khỏi database. Viết
            // `.Include(o => o.Items)` rồi `.Sum()` trong C# cho ra CÙNG con số nhưng
            // kéo về toàn bộ dòng đơn của cả trang - đúng cái N+1 mà không ai để ý vì
            // kết quả vẫn đúng.
            .Select(o => new OrderSummary(
                o.Id,
                o.CreatedAt,
                o.TotalAmount,
                o.Status,
                o.Items.Sum(i => i.Quantity)))

            .ToListAsync(cancellationToken);

        return new PagedResult<OrderSummary>(items, totalCount, page, pageSize);
    }

    public async Task<Order?> GetByIdForUserAsync(
        int orderId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        // Điều kiện UserId nằm TRONG câu truy vấn, không kiểm tra sau khi đọc lên.
        // Đọc rồi so `order.UserId != userId` cũng chặn được, nhưng nó biến một truy
        // vấn không tìm thấy gì thành một truy vấn có tải dữ liệu người khác về bộ
        // nhớ tiến trình - chỉ cần một lần quên `if` là rò rỉ.
        return await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == orderId && o.UserId == userId, cancellationToken);
    }

    public Task<bool> HasOrdersForProductAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        // AnyAsync sinh ra EXISTS, dừng ngay khi gặp dòng đầu tiên - không đếm hết
        // và không tải dòng nào về.
        return _context.OrderDetails
            .AnyAsync(d => d.ProductId == productId, cancellationToken);
    }
}
