using MiniMart.Application.Interfaces;
using MiniMart.Application.Models;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Domain.ValueObjects;

namespace MiniMart.Application.Services;

/// <summary>
/// Toàn bộ nghiệp vụ giỏ hàng, viết MỘT lần cho cả hai kho lưu trữ.
///
/// <para>
/// Nó chỉ nói chuyện với <see cref="ICartStore"/> nên không hề biết dữ liệu nằm ở
/// Session hay DB. Đó là lý do những quy tắc dưới đây - cộng dồn, kẹp theo tồn
/// kho, lọc sản phẩm đã xoá - không bị nhân đôi.
/// </para>
/// </summary>
public class CartService : ICartService
{
    private readonly ICartStore _cartStore;
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CartService(
        ICartStore cartStore,
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _cartStore = cartStore;
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<CartView> GetCartAsync(CancellationToken cancellationToken = default)
    {
        var lines = await _cartStore.GetLinesAsync(cancellationToken);

        return await DungViewAsync(lines, cancellationToken);
    }

    public async Task<CartMutationResult> AddAsync(
        int productId,
        int quantity = 1,
        CancellationToken cancellationToken = default)
    {
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "Số lượng thêm vào giỏ phải >= 1.");
        }

        var product = await LaySanPhamAsync(productId, cancellationToken);

        // Hết sạch thì ném; còn ít hơn yêu cầu thì KẸP (xử lý bên dưới). Kẹp về 0
        // sẽ là "bấm nút mà không có gì xảy ra" - phản hồi tệ nhất có thể.
        if (product.Stock <= 0)
        {
            throw new OutOfStockException(product.Id, product.Name);
        }

        var lines = await _cartStore.GetLinesAsync(cancellationToken);
        var dangCo = lines.FirstOrDefault(l => l.ProductId == productId)?.Quantity ?? 0;

        // CỘNG DỒN, không ghi đè: bấm "thêm vào giỏ" hai lần với số lượng 1 phải
        // ra 2. Ghi đè là ngữ nghĩa của UpdateQuantityAsync, không phải của đây.
        var mongMuon = dangCo + quantity;
        var thucTe = Math.Min(mongMuon, product.Stock);

        await _cartStore.SetQuantityAsync(productId, thucTe, cancellationToken);
        await LuuAsync(cancellationToken);

        var notice = thucTe < mongMuon
            ? $"Sản phẩm '{product.Name}' chỉ còn {product.Stock}. " +
              $"Số lượng trong giỏ đã được đặt về {thucTe}."
            : null;

        return new CartMutationResult(await GetCartAsync(cancellationToken), notice);
    }

    public async Task<CartMutationResult> UpdateQuantityAsync(
        int productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "Số lượng không được âm.");
        }

        // 0 = xoá dòng. Đây là ngữ nghĩa của ô nhập số lượng trên trang giỏ hàng:
        // người dùng sửa về 0 tức là không mua nữa. ICartStore cố ý KHÔNG nhận 0
        // (DB có CHECK Quantity > 0), nên việc dịch 0 thành "xoá" nằm ở đây.
        if (quantity == 0)
        {
            return await RemoveAsync(productId, cancellationToken);
        }

        var product = await LaySanPhamAsync(productId, cancellationToken);

        if (product.Stock <= 0)
        {
            throw new OutOfStockException(product.Id, product.Name);
        }

        var thucTe = Math.Min(quantity, product.Stock);

        await _cartStore.SetQuantityAsync(productId, thucTe, cancellationToken);
        await LuuAsync(cancellationToken);

        var notice = thucTe < quantity
            ? $"Sản phẩm '{product.Name}' chỉ còn {product.Stock}. " +
              $"Số lượng trong giỏ đã được đặt về {thucTe}."
            : null;

        return new CartMutationResult(await GetCartAsync(cancellationToken), notice);
    }

    public async Task<CartMutationResult> RemoveAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        // Không kiểm tra sản phẩm có tồn tại: xoá được dòng trỏ tới sản phẩm ĐÃ
        // BỊ XOÁ là điều bắt buộc, nếu không thì giỏ Session mắc kẹt vĩnh viễn
        // với một dòng không xoá nổi.
        await _cartStore.RemoveAsync(productId, cancellationToken);
        await LuuAsync(cancellationToken);

        return new CartMutationResult(await GetCartAsync(cancellationToken));
    }

    public async Task MergeAsync(
        ICartStore nguon,
        ICartStore dich,
        CancellationToken cancellationToken = default)
    {
        var dongNguon = await nguon.GetLinesAsync(cancellationToken);

        if (dongNguon.Count == 0)
        {
            return;
        }

        var dongDich = await dich.GetLinesAsync(cancellationToken);

        var sanPham = await LayTheoIdAsync(
            dongNguon.Select(l => l.ProductId).Concat(dongDich.Select(l => l.ProductId)),
            cancellationToken);

        foreach (var dong in dongNguon)
        {
            // Sản phẩm đã bị xoá khỏi shop trong lúc giỏ Session còn sống -> bỏ qua.
            if (!sanPham.TryGetValue(dong.ProductId, out var product) || product.Stock <= 0)
            {
                continue;
            }

            var daCo = dongDich.FirstOrDefault(l => l.ProductId == dong.ProductId)?.Quantity ?? 0;

            // TỔNG hai bên, rồi kẹp theo tồn kho.
            //
            // Chọn tổng chứ không phải max: người dùng đã chủ động thêm ở cả hai
            // nơi nên cả hai ý định đều thật. Lấy max sẽ âm thầm bỏ một trong hai.
            var tong = Math.Min(daCo + dong.Quantity, product.Stock);

            await dich.SetQuantityAsync(dong.ProductId, tong, cancellationToken);
        }

        // Thứ tự BẮT BUỘC: chốt giỏ đích TRƯỚC, xoá giỏ nguồn SAU.
        //
        // Cùng logic với thứ tự ghi/xoá file ảnh: nếu lưu thất bại thì giỏ nguồn
        // vẫn còn (lần đăng nhập sau gộp lại được). Làm ngược lại mà lưu thất bại
        // là người dùng mất sạch giỏ hàng - hỏng nặng hơn nhiều so với rủi ro gộp
        // hai lần (số lượng bị cộng đôi nhưng vẫn bị kẹp theo tồn kho).
        await LuuAsync(cancellationToken);
        await nguon.ClearAsync(cancellationToken);
    }

    // ───────────── Helper ─────────────

    /// <summary>
    /// Với kho DB thì đây là lệnh chốt thật; với kho Session thì Session đã được
    /// ghi ngay trong <c>SetQuantityAsync</c> nên lệnh này không làm gì (0 thay
    /// đổi được theo dõi). Bất đối xứng đã ghi rõ trên <see cref="ICartStore"/>.
    /// </summary>
    private Task LuuAsync(CancellationToken cancellationToken) =>
        _unitOfWork.SaveChangesAsync(cancellationToken);

    private async Task<Product> LaySanPhamAsync(int productId, CancellationToken cancellationToken) =>
        await _productRepository.GetByIdAsync(productId, cancellationToken)
        ?? throw new NotFoundException(nameof(Product), productId);

    private async Task<Dictionary<int, Product>> LayTheoIdAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken)
    {
        var sanPham = await _productRepository.GetByIdsAsync(
            ids.Distinct(), cancellationToken);

        return sanPham.ToDictionary(p => p.Id);
    }

    /// <summary>
    /// Ghép CartLine (chỉ có ProductId + Quantity) với dữ liệu sản phẩm.
    /// MỘT truy vấn cho cả giỏ, không phải một truy vấn mỗi dòng.
    /// </summary>
    private async Task<CartView> DungViewAsync(
        IReadOnlyList<CartLine> lines,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return CartView.Empty;
        }

        var sanPham = await LayTheoIdAsync(lines.Select(l => l.ProductId), cancellationToken);

        var view = lines
            // Lọc bỏ dòng trỏ tới sản phẩm không còn tồn tại. KHÔNG xoá khỏi kho:
            // đây là đường đọc và GET phải là thao tác an toàn.
            .Where(l => sanPham.ContainsKey(l.ProductId))
            .Select(l =>
            {
                var product = sanPham[l.ProductId];

                return new CartLineView(
                    product.Id,
                    product.Name,
                    product.ImageUrl,
                    // Giá HIỆN TẠI, không phải giá lúc thêm vào giỏ.
                    product.Price,
                    l.Quantity,
                    product.Stock);
            })
            .ToList();

        return new CartView(view);
    }
}
