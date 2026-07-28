using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace MiniMart.Web.Models;

/// <summary>
/// Bộ lọc sản phẩm phía khách hàng, dùng CHUNG cho HomeController.Index
/// (render trang 1) và ProductController.LoadMore (trang 2+).
///
/// <para>
/// Lý do gộp thành một type thay vì để mỗi action khai báo 3 tham số riêng:
/// hai action BẮT BUỘC phải nhận cùng một bộ lọc, mà hai danh sách tham số
/// giống nhau thì không có gì ngăn chúng lệch nhau khi thêm filter mới. Thêm
/// một property ở đây là cả hai action nhận được ngay - trình biên dịch không
/// cho phép quên một bên.
/// </para>
/// <para>
/// Model binding lấy giá trị từ query string theo TÊN PROPERTY không tiền tố
/// (<c>?categoryId=5&amp;minPrice=1000</c>), nên URL vẫn sạch và bookmark được.
/// </para>
/// </summary>
public class ProductFilter : IValidatableObject
{
    [StringLength(100, ErrorMessage = "Từ khoá tìm kiếm không được quá 100 ký tự.")]
    public string? Search { get; set; }

    public int? CategoryId { get; set; }

    // Ràng buộc thuộc về BẢN THÂN giá trị (giá không thể âm) -> Data Annotation,
    // không phải Service. Dùng overload typeof(decimal) + chuỗi vì overload
    // Range(0, ...) nhận double và làm tròn nhị phân trước khi so với decimal.
    //
    // ParseLimitsInInvariantCulture là cờ chi phối việc parse hai chuỗi CẬN; thiếu nó
    // thì máy vi-VN ném ArgumentException lúc validate. Xem rules/data-access.md.
    [Range(typeof(decimal), "0", "999999999",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true,
        ErrorMessage = "Giá từ phải là số không âm.")]
    [Display(Name = "Giá từ")]
    public decimal? MinPrice { get; set; }

    [Range(typeof(decimal), "0", "999999999",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true,
        ErrorMessage = "Giá đến phải là số không âm.")]
    [Display(Name = "Giá đến")]
    public decimal? MaxPrice { get; set; }

    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(Search)
        || CategoryId is not null
        || MinPrice is not null
        || MaxPrice is not null;

    /// <summary>
    /// Chuỗi InvariantCulture để đổ vào <c>value</c> của input và <c>data-*</c>
    /// của nút "Xem thêm". Không dùng ToString() mặc định: máy locale vi-VN sẽ
    /// in "1000,5" và lần submit sau model binding không parse lại được.
    /// </summary>
    public string? MinPriceText => MinPrice?.ToString(CultureInfo.InvariantCulture);

    public string? MaxPriceText => MaxPrice?.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Ràng buộc LIÊN QUAN HAI THUỘC TÍNH nên không đặt được bằng attribute trên
    /// một property đơn lẻ. IValidatableObject chạy SAU khi từng property đã hợp
    /// lệ, nên ở đây không phải kiểm tra null hay kiểu dữ liệu nữa.
    ///
    /// Đặt ở đây chứ không ở Service vì nó không cần DB, không cần async, không
    /// cần dependency nào - đúng tiêu chí phân loại của dự án.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice.HasValue && MaxPrice.HasValue && MinPrice > MaxPrice)
        {
            // Khoảng giá ngược là truy vấn HỢP LỆ và trả về rỗng một cách đúng
            // đắn, nên nếu không báo thì người dùng sẽ tưởng shop hết hàng.
            yield return new ValidationResult(
                "Khoảng giá không hợp lệ: giá từ lớn hơn giá đến.",
                [nameof(MaxPrice)]);
        }
    }
}
