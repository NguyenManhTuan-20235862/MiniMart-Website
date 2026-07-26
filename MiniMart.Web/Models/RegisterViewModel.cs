using System.ComponentModel.DataAnnotations;

namespace MiniMart.Web.Models;

public class RegisterViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Tên đăng nhập phải từ 3 đến 50 ký tự.")]
    [Display(Name = "Tên đăng nhập")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
    // Yêu cầu tối thiểu chữ + số. Không đòi ký tự đặc biệt: nghiên cứu của NIST
    // (SP 800-63B) cho thấy quy tắc phức tạp khiến người dùng chọn kiểu
    // "Password1!" - dễ đoán hơn một mật khẩu dài. Độ DÀI mới là yếu tố chính,
    // nên trần 100 ký tự ở trên cố ý để rộng cho passphrase.
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$",
        ErrorMessage = "Mật khẩu phải chứa cả chữ và số.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    [Display(Name = "Xác nhận mật khẩu")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
