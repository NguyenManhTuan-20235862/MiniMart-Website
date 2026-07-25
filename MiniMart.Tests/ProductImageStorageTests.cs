using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using MiniMart.Web.Services;
using Moq;

namespace MiniMart.Tests;

/// <summary>
/// Test ghi file thật, nhưng trong thư mục tạm riêng của từng lần chạy nên
/// không đụng tới wwwroot của dự án.
/// </summary>
public class ProductImageStorageTests : IDisposable
{
    private readonly string _webRoot =
        Path.Combine(Path.GetTempPath(), $"minimart_test_{Guid.NewGuid():N}");

    private WebRootProductImageStorage CreateSut()
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.WebRootPath).Returns(_webRoot);

        return new WebRootProductImageStorage(environment.Object);
    }

    private static IFormFile TaoFile(string fileName)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("noi dung anh gia"));
        return new FormFile(stream, 0, stream.Length, "ImageFile", fileName);
    }

    [Fact]
    public async Task SaveAsync_KHONG_duoc_dung_ten_file_nguoi_dung_gui_len()
    {
        var sut = CreateSut();

        var url = await sut.SaveAsync(TaoFile("anh-cua-toi.jpg"));

        // Giữ tên gốc là mở cửa cho path traversal và ghi đè lẫn nhau.
        Assert.DoesNotContain("anh-cua-toi", url);
    }

    [Fact]
    public async Task SaveAsync_giu_phan_mo_rong_va_tra_ve_duong_dan_web()
    {
        var sut = CreateSut();

        var url = await sut.SaveAsync(TaoFile("anh.png"));

        Assert.StartsWith("/images/products/", url);
        Assert.EndsWith(".png", url);
        // Đường dẫn URL dùng '/', không phải dấu phân cách của hệ điều hành.
        Assert.DoesNotContain("\\", url);
    }

    [Fact]
    public async Task SaveAsync_ghi_file_that_xuong_dia()
    {
        var sut = CreateSut();

        var url = await sut.SaveAsync(TaoFile("anh.jpg"));
        var fullPath = Path.Combine(_webRoot, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(fullPath));
    }

    [Fact]
    public async Task SaveAsync_hai_lan_cung_ten_ra_hai_file_khac_nhau()
    {
        var sut = CreateSut();

        var url1 = await sut.SaveAsync(TaoFile("anh.jpg"));
        var url2 = await sut.SaveAsync(TaoFile("anh.jpg"));

        // Tên GUID nên hai lần upload cùng tên không ghi đè lên nhau.
        Assert.NotEqual(url1, url2);
    }

    [Fact]
    public async Task Delete_xoa_duoc_file_da_luu()
    {
        var sut = CreateSut();
        var url = await sut.SaveAsync(TaoFile("anh.jpg"));
        var fullPath = Path.Combine(_webRoot, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        sut.Delete(url);

        Assert.False(File.Exists(fullPath));
    }

    [Theory]
    [InlineData("/appsettings.json")]
    [InlineData("/../../appsettings.json")]
    [InlineData("/images/other/anh.jpg")]
    public void Delete_bo_qua_duong_dan_nam_ngoai_thu_muc_anh(string url)
    {
        var sut = CreateSut();
        var duongDanNhayCam = Path.Combine(_webRoot, "appsettings.json");
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(duongDanNhayCam, "khong duoc xoa");

        sut.Delete(url);

        // Nếu ImageUrl trong DB bị sửa, lệnh xoá không được biến thành công cụ
        // xoá file bất kỳ trên server.
        Assert.True(File.Exists(duongDanNhayCam));
    }

    [Fact]
    public void Delete_voi_null_hoac_rong_thi_khong_nem_loi()
    {
        var sut = CreateSut();

        sut.Delete(null);
        sut.Delete("");
    }

    public void Dispose()
    {
        if (Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }
}
