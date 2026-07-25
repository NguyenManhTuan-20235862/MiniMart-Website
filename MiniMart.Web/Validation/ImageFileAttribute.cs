using System.ComponentModel.DataAnnotations;

namespace MiniMart.Web.Validation;

/// <summary>
/// Kiểm tra phần mở rộng và dung lượng file tải lên.
///
/// Đây là ví dụ Data Annotation dùng ĐÚNG chỗ (đối lập với CategoryId ở phase
/// trước): cả hai ràng buộc đều thuộc về bản thân giá trị, đánh giá được ngay
/// mà không cần truy vấn DB, không cần async, không cần DI.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ImageFileAttribute : ValidationAttribute
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public int MaxSizeInMb { get; init; } = 2;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Không chọn file là hợp lệ - ảnh không bắt buộc.
        if (value is not IFormFile file || file.Length == 0)
        {
            return ValidationResult.Success;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        // Whitelist chứ không phải blacklist: liệt kê cái được phép an toàn hơn
        // nhiều so với cố đoán hết những cái nguy hiểm.
        if (!AllowedExtensions.Contains(extension))
        {
            return new ValidationResult(
                $"Chỉ chấp nhận file {string.Join(", ", AllowedExtensions)}.",
                [validationContext.MemberName!]);
        }

        if (file.Length > MaxSizeInMb * 1024L * 1024L)
        {
            return new ValidationResult(
                $"Ảnh không được vượt quá {MaxSizeInMb}MB.",
                [validationContext.MemberName!]);
        }

        return ValidationResult.Success;
    }
}
