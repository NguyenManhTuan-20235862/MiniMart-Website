namespace MiniMart.Web.Services;

public class WebRootProductImageStorage : IProductImageStorage
{
    private const string RelativeFolder = "images/products";

    private readonly IWebHostEnvironment _environment;

    public WebRootProductImageStorage(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(_environment.WebRootPath, RelativeFolder);
        Directory.CreateDirectory(folder);

        // TÊN FILE DO SERVER SINH RA, tuyệt đối không dùng file.FileName.
        // Tên người dùng gửi lên có thể là "../../appsettings.json" (path
        // traversal) hoặc trùng tên file đã có (ghi đè lẫn nhau).
        // Chỉ lấy phần mở rộng, và đã được whitelist ở tầng validation.
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid():N}{extension}";

        var fullPath = Path.Combine(folder, fileName);

        await using var stream = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(stream, cancellationToken);

        // Dùng dấu / vì đây là URL, không phải đường dẫn hệ điều hành.
        return $"/{RelativeFolder}/{fileName}";
    }

    public void Delete(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        // Chỉ chấp nhận đường dẫn do chính SaveAsync sinh ra - chặn việc một
        // ImageUrl bị sửa trong DB biến lệnh xoá này thành công cụ xoá file bất kỳ.
        if (!imageUrl.StartsWith($"/{RelativeFolder}/", StringComparison.Ordinal))
        {
            return;
        }

        var fullPath = Path.Combine(
            _environment.WebRootPath,
            imageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}
