using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Http;
using MiniMart.Web.Validation;

namespace MiniMart.Tests;

public class ImageFileValidationTests
{
    private sealed class Model
    {
        [ImageFile(MaxSizeInMb = 2)]
        public IFormFile? ImageFile { get; set; }
    }

    private static IFormFile TaoFile(string fileName, int sizeInBytes = 1024)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', sizeInBytes)));
        return new FormFile(stream, 0, stream.Length, "ImageFile", fileName);
    }

    private static bool HopLe(IFormFile? file)
    {
        var model = new Model { ImageFile = file };
        var results = new List<ValidationResult>();

        return Validator.TryValidateObject(
            model, new ValidationContext(model), results, validateAllProperties: true);
    }

    [Fact]
    public void Khong_chon_file_la_hop_le()
    {
        // Ảnh không bắt buộc.
        Assert.True(HopLe(null));
    }

    [Theory]
    [InlineData("anh.jpg")]
    [InlineData("anh.jpeg")]
    [InlineData("anh.png")]
    [InlineData("anh.webp")]
    [InlineData("anh.JPG")] // hoa/thường không được phép làm lọt whitelist
    public void Dinh_dang_trong_whitelist_thi_hop_le(string fileName)
    {
        Assert.True(HopLe(TaoFile(fileName)));
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("shell.php")]
    [InlineData("script.svg")]   // SVG chứa được JavaScript -> XSS
    [InlineData("anh.jpg.exe")]  // phần mở rộng thật là cái CUỐI cùng
    public void Dinh_dang_ngoai_whitelist_thi_bi_tu_choi(string fileName)
    {
        Assert.False(HopLe(TaoFile(fileName)));
    }

    [Fact]
    public void File_vuot_qua_2MB_thi_bi_tu_choi()
    {
        Assert.False(HopLe(TaoFile("anh.jpg", sizeInBytes: 3 * 1024 * 1024)));
    }
}
