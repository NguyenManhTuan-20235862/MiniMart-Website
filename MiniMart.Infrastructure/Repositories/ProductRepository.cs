using Microsoft.EntityFrameworkCore;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly MiniMartDbContext _context;

    public ProductRepository(MiniMartDbContext context)
    {
        _context = context;
    }

    public async Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Products
            // Không Include thì p.Category là null -> view gọi Category.Name sẽ nổ.
            .Include(p => p.Category)
            // Chỉ đọc để hiển thị -> bỏ qua Change Tracker cho nhẹ.
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Product>> GetByCategoryAsync(
        int categoryId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Include(p => p.Category)
            .AsNoTracking()
            // Where đặt TRƯỚC ToListAsync nên điều kiện được dịch thành SQL và
            // lọc dưới DB. Gọi ToListAsync trước rồi mới Where sẽ kéo cả bảng
            // lên bộ nhớ rồi mới lọc.
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Include(p => p.Category)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default)
    {
        // CÓ tracking: Change Tracker phải giữ giá trị RowVersion gốc thì
        // SaveChanges mới kẹp được nó vào WHERE để phát hiện xung đột.
        return await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        await _context.Products.AddAsync(product, cancellationToken);
    }

    public void Remove(Product product)
    {
        // Không async vì chỉ đánh dấu Deleted trong bộ nhớ, chưa chạm DB.
        _context.Products.Remove(product);
    }
}
