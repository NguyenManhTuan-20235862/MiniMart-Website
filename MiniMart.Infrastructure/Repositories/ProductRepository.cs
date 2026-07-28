using Microsoft.EntityFrameworkCore;
using MiniMart.Common;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Domain.ValueObjects;
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
        string? search = null,
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

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(p => p.Name.Contains(keyword));
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

        // ★ Kẹp CẬN TRÊN, và phải làm SAU lệnh đếm vì trước đó chưa biết có bao nhiêu trang.
        //
        // Thiếu bước này thì `?page=999` cho ra một trang RỖNG, mà view không phân biệt
        // được "trang này rỗng" với "chưa có sản phẩm nào" nên nó in ra câu thứ hai -
        // sai sự thật khi trong kho có 50 sản phẩm. Tệ hơn: bộ phân trang chỉ hiện khi
        // có nhiều hơn một trang, mà trang rỗng thì không có nó, nên người dùng rơi vào
        // NGÕ CỤT và chỉ thoát được bằng cách tự sửa URL.
        //
        // Đặt ở đây chứ không ở view: view không nên biết cách sửa một tham số hỏng, và
        // đặt ở Controller là mỗi Controller phải nhớ làm lại. Đúng tinh thần của lệnh
        // kẹp `page < 1` ngay bên trên - chỉ là nửa còn thiếu của nó.
        //
        // KHÔNG tốn thêm round-trip nào: lệnh đếm vốn đã chạy trước lệnh lấy dữ liệu.
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        if (totalCount > 0 && page > totalPages)
        {
            page = totalPages;
        }

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

    public async Task<List<Product>> GetByIdsAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken = default)
    {
        // Vật chất hoá thành mảng trước: `ids` có thể là một IEnumerable lười
        // (VD kết quả của Select), mà EF Core cần đếm được để dựng câu IN (...).
        var danhSach = ids as int[] ?? ids.ToArray();

        if (danhSach.Length == 0)
        {
            // Tránh gửi "WHERE Id IN ()" xuống DB - vừa vô nghĩa vừa tốn round-trip.
            return [];
        }

        return await _context.Products
            .Include(p => p.Category)
            .AsNoTracking()
            // Contains được EF Core dịch thành `WHERE [p].[Id] IN (@p0, @p1, ...)`.
            .Where(p => danhSach.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Product>> GetRelatedAsync(
        int productId,
        int categoryId,
        int take = 4,
        CancellationToken cancellationToken = default)
    {
        // Kẹp y như pageSize: tham số này hôm nay do Controller truyền hằng số, nhưng
        // một hằng số hôm nay là một giá trị từ query string ngày mai.
        take = Math.Clamp(take, 1, 12);

        return await _context.Products
            .Include(p => p.Category)
            .AsNoTracking()
            // Trừ chính sản phẩm đang xem: gợi ý người ta quay lại đúng trang họ
            // đang đứng là lỗi không ai báo, chỉ trông rất ngớ ngẩn.
            .Where(p => p.CategoryId == categoryId && p.Id != productId)
            // Còn hàng lên trước. EF Core dịch biểu thức bool này thành
            // `ORDER BY CASE WHEN [p].[Stock] > 0 THEN 1 ELSE 0 END DESC` - việc sắp
            // xếp xảy ra dưới SQL Server, trước khi TOP cắt bớt. Sắp trong C# sau
            // ToListAsync thì thứ tự chỉ đúng TRONG 4 dòng đã bị cắt sai.
            .OrderByDescending(p => p.Stock > 0)
            .ThenBy(p => p.Name)
            // Tie-breaker BẮT BUỘC: hai sản phẩm cùng tình trạng hàng và trùng tên
            // thì SQL Server được tự do trả về thứ tự khác nhau giữa các lần chạy,
            // và TOP 4 sẽ cắt ra hai bộ khác nhau.
            .ThenBy(p => p.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Collation dùng riêng cho việc TÌM KIẾM, không đổi collation của cột.
    ///
    /// <para>
    /// <c>CI</c> = không phân biệt hoa/thường, <c>AI</c> = <b>không phân biệt dấu</b>.
    /// Database đang dùng <c>SQL_Latin1_General_CP1_CI_AS</c> — chữ <c>AS</c> cuối
    /// nghĩa là CÓ phân biệt dấu, nên `LIKE N'%chuot%'` trả về 0 dòng trong khi shop
    /// đang bán "Chuột Logitech MX Master 3S". Đã đo trực tiếp bằng sqlcmd.
    /// </para>
    /// <para>
    /// ⚠ Đánh đổi phải biết: ép collation trong <c>WHERE</c> làm biểu thức không còn
    /// <i>sargable</i> — SQL Server không dùng được index trên <c>Name</c> nữa và phải
    /// quét bảng. Với quy mô hiện tại (50 sản phẩm) thì không đáng bàn. Khi kho lớn
    /// lên, cách đúng là thêm một cột tính toán đã chuẩn hoá (persisted) mang sẵn
    /// collation này rồi đánh index lên nó — KHÔNG phải bỏ lệnh ép collation đi.
    /// </para>
    /// </summary>
    private const string KieuSoSanhTimKiem = "Vietnamese_CI_AI";

    /// <summary>Dưới ngưỡng này thì gợi ý là nhiễu chứ không phải trợ giúp.</summary>
    private const int DoDaiToiThieu = 2;

    public async Task<List<ProductSuggestion>> SuggestAsync(
        string? tuKhoa,
        int take = 8,
        CancellationToken cancellationToken = default)
    {
        var kw = tuKhoa?.Trim() ?? string.Empty;

        // Trả rỗng thay vì trả cả kho: một ký tự khớp gần như mọi thứ, và dropdown
        // 8 dòng ngẫu nhiên thì tệ hơn là không có dropdown nào.
        if (kw.Length < DoDaiToiThieu)
        {
            return [];
        }

        take = Math.Clamp(take, 1, 20);

        // Ghép chuỗi TRƯỚC khi vào cây biểu thức. Viết `" " + kw` ngay trong truy vấn
        // thì EF phải dịch phép nối chuỗi sang SQL - chạy được, nhưng đây là hằng số
        // đối với câu truy vấn nên tính sẵn vừa rõ vừa tham số hoá được.
        var kwSauKhoangTrang = " " + kw;

        return await _context.Products
            .AsNoTracking()
            .Where(p => EF.Functions.Collate(p.Name, KieuSoSanhTimKiem).Contains(kw))
            // ── Xếp hạng "giống nhất lên trước" ───────────────────────────────────
            // Biểu thức ba nhánh này được EF dịch thành CASE WHEN trong ORDER BY, nên
            // việc sắp xếp xảy ra DƯỚI SQL Server - trước khi TOP cắt bớt. Sắp trong
            // C# sau ToListAsync thì thứ tự chỉ đúng trong 8 dòng đã bị cắt sai.
            .OrderBy(p =>
                EF.Functions.Collate(p.Name, KieuSoSanhTimKiem).StartsWith(kw) ? 0
                : EF.Functions.Collate(p.Name, KieuSoSanhTimKiem).Contains(kwSauKhoangTrang) ? 1
                : 2)
            // Đồng hạng thì tên NGẮN hơn lên trước: từ khoá chiếm tỉ lệ lớn hơn trong
            // một tên ngắn nên nó "giống" hơn theo cảm nhận của người gõ.
            .ThenBy(p => p.Name.Length)
            .ThenBy(p => p.Name)
            // Tie-breaker duy nhất, BẮT BUỘC với Take y như với Skip/Take: thiếu nó
            // thì hai lần gõ cùng một từ khoá có thể ra hai danh sách khác nhau.
            .ThenBy(p => p.Id)
            .Take(take)
            // Chiếu NGAY trong truy vấn: Stock và RowVersion không rời khỏi database.
            .Select(p => new ProductSuggestion(
                p.Id, p.Name, p.Price, p.ImageUrl, p.Category.Name, p.Stock > 0))
            .ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default)
    {
        // CÓ tracking: Change Tracker phải giữ giá trị RowVersion gốc thì
        // SaveChanges mới kẹp được nó vào WHERE để phát hiện xung đột.
        return await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<List<Product>> GetManyForUpdateAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken = default)
    {
        var danhSach = ids as int[] ?? ids.ToArray();

        if (danhSach.Length == 0)
        {
            return [];
        }

        // CÓ tracking (không AsNoTracking) — khác GetByIdsAsync ở đúng điểm này.
        // Không có tracking thì Change Tracker không giữ RowVersion gốc, phép trừ
        // tồn kho không được lưu, và không xung đột nào bị phát hiện.
        //
        // Không Include(Category): đường ghi chỉ cần Name, Price, Stock, RowVersion.
        return await _context.Products
            .Where(p => danhSach.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public void SetExpectedRowVersion(Product product, byte[] rowVersion)
    {
        // Ghi vào OriginalValue, KHÔNG phải CurrentValue.
        //
        // EF Core sinh câu UPDATE dạng:
        //     UPDATE Products SET ... WHERE Id = @id AND RowVersion = @original
        // Giá trị kẹp vào WHERE là OriginalValue. CurrentValue của cột rowversion
        // do SQL Server tự sinh nên gán vào đó không có tác dụng gì.
        //
        // Sau GetForUpdateAsync, OriginalValue = phiên bản HIỆN TẠI trong DB nên
        // WHERE luôn khớp. Ghi đè bằng phiên bản người dùng thấy lúc mở form thì
        // WHERE mới trượt được khi có người khác đã sửa ở giữa.
        _context.Entry(product).Property(p => p.RowVersion).OriginalValue = rowVersion;
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
