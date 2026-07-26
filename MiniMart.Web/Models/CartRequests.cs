using System.ComponentModel.DataAnnotations;

namespace MiniMart.Web.Models;

/// <summary>
/// Ràng buộc thuộc về BẢN THÂN giá trị nên đặt bằng Data Annotation, không phải
/// ở Service: đánh giá được mà không cần truy vấn DB, không cần async, không cần DI.
/// "Sản phẩm có tồn tại không" và "còn đủ hàng không" mới nằm ở CartService.
/// </summary>
public class AddToCartRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Mã sản phẩm không hợp lệ.")]
    public int ProductId { get; set; }

    // Trần 100 chứ không phải int.MaxValue: CartService cộng dồn số lượng mới vào
    // số đang có, nên ?quantity=2147483647 sẽ làm phép cộng TRÀN SỐ và ra số âm.
    // Chặn ở biên đầu vào là chỗ rẻ nhất.
    [Range(1, 100, ErrorMessage = "Số lượng phải từ 1 đến 100.")]
    public int Quantity { get; set; } = 1;
}

public class UpdateCartQuantityRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Mã sản phẩm không hợp lệ.")]
    public int ProductId { get; set; }

    // Cho phép 0: trên trang giỏ hàng, sửa số lượng về 0 nghĩa là bỏ món đó.
    // CartService dịch 0 thành thao tác xoá.
    [Range(0, 100, ErrorMessage = "Số lượng phải từ 0 đến 100.")]
    public int Quantity { get; set; }
}

public class RemoveFromCartRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Mã sản phẩm không hợp lệ.")]
    public int ProductId { get; set; }
}
