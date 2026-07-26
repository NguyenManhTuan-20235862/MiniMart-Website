namespace MiniMart.Common.Exceptions;

/// <summary>
/// Không xoá được sản phẩm vì đã có đơn hàng chứa nó.
///
/// <para>
/// Đây là hệ quả CỐ Ý của <c>OrderDetails.ProductId</c> dùng <c>DeleteBehavior.Restrict</c>:
/// đơn hàng là bản ghi tài chính, xoá sản phẩm mà kéo mất dòng đơn là để lịch sử bán
/// hàng tự sửa lại chính nó.
/// </para>
/// <para>
/// Song song đó, <c>CartItems.ProductId</c> lại dùng <c>Cascade</c> - sản phẩm bị xoá
/// tự biến khỏi mọi giỏ hàng. Hai hành vi ngược nhau trên cùng một khoá ngoại là
/// đúng: giỏ hàng là dữ liệu tạm, đơn hàng thì không.
/// </para>
/// </summary>
public class ProductHasOrdersException : Exception
{
    public ProductHasOrdersException(int productId, Exception? innerException = null)
        : base("Không thể xoá sản phẩm này vì đã có đơn hàng chứa nó. " +
               "Hãy đặt tồn kho về 0 để ngừng bán thay vì xoá.",
            innerException)
    {
        ProductId = productId;
    }

    public int ProductId { get; }
}
