using Microsoft.EntityFrameworkCore;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
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
