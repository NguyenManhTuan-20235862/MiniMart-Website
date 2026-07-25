using Microsoft.EntityFrameworkCore;
using MiniMart.Common;
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

    public async Task<PagedResult<Product>> GetProductsAsync(
        int? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int page = 1,
        int pageSize = 12,
        CancellationToken cancellationToken = default)
    {
        // Chặn tham số bậy từ query string: ?page=-5&pageSize=999999
        if (page < 1)
        {
            page = 1;
        }

        pageSize = Math.Clamp(pageSize, 1, 100);

        // Từ đây tới ToListAsync KHÔNG có lệnh nào chạm database. Mỗi lệnh chỉ
        // gắn thêm một nhánh vào cây biểu thức.
        IQueryable<Product> query = _context.Products
            .Include(p => p.Category)
            .AsNoTracking();

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (minPrice.HasValue)
        {
            query = query.Where(p => p.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(p => p.Price <= maxPrice.Value);
        }

        // Query thứ nhất: đếm tổng số bản ghi KHỚP BỘ LỌC (chưa phân trang).
        var totalCount = await query.CountAsync(cancellationToken);

        // Query thứ hai: lấy đúng một trang. Dùng lại cùng biến query - đó là
        // lợi ích của deferred execution.
        var items = await query
            .OrderBy(p => p.Name)
            // ThenBy(Id) là tie-breaker BẮT BUỘC: hai sản phẩm trùng tên mà
            // không có thứ tự phụ thì SQL Server được tự do sắp xếp khác nhau
            // giữa các lần chạy, khiến một bản ghi hiện ở cả trang 1 lẫn trang 2
            // trong khi một bản ghi khác biến mất.
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Product>(items, totalCount, page, pageSize);
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
