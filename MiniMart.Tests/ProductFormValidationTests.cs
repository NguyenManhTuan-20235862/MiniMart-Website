using System.ComponentModel.DataAnnotations;
using MiniMart.Web.Areas.Admin.Models;

namespace MiniMart.Tests;

/// <summary>
/// Kiểm chứng tầng Data Annotation của ProductFormViewModel.
/// Chạy Validator trực tiếp - không cần HTTP, không cần DB.
/// </summary>
public class ProductFormValidationTests
{
    private static ProductFormViewModel TaoModelHopLe() => new()
    {
        Name = "ThinkPad X1",
        Price = 32_000_000m,
        Stock = 5,
        CategoryId = 1
    };

    private static List<ValidationResult> Validate(ProductFormViewModel model)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true);

        return results;
    }

    private static bool CoLoiO(List<ValidationResult> results, string propertyName) =>
        results.Any(r => r.MemberNames.Contains(propertyName));

    [Fact]
    public void Model_hop_le_thi_khong_co_loi_nao()
    {
        Assert.Empty(Validate(TaoModelHopLe()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_rong_thi_bao_loi(string name)
    {
        var model = TaoModelHopLe();
        model.Name = name;

        Assert.True(CoLoiO(Validate(model), nameof(ProductFormViewModel.Name)));
    }

    [Theory]
    [InlineData(0)]        // ranh giới: phải > 0, không phải >= 0
    [InlineData(-1)]
    public void Price_khong_lon_hon_0_thi_bao_loi(int price)
    {
        var model = TaoModelHopLe();
        model.Price = price;

        Assert.True(CoLoiO(Validate(model), nameof(ProductFormViewModel.Price)));
    }

    [Fact]
    public void Price_bang_0_01_la_hop_le()
    {
        var model = TaoModelHopLe();
        model.Price = 0.01m;

        // Nếu ConvertValueInInvariantCulture bị bỏ và máy chạy locale vi-VN,
        // "0.01" bị parse sai và test này sẽ đỏ.
        Assert.False(CoLoiO(Validate(model), nameof(ProductFormViewModel.Price)));
    }

    [Fact]
    public void Stock_am_thi_bao_loi()
    {
        var model = TaoModelHopLe();
        model.Stock = -1;

        Assert.True(CoLoiO(Validate(model), nameof(ProductFormViewModel.Stock)));
    }

    [Fact]
    public void Stock_bang_0_la_hop_le()
    {
        var model = TaoModelHopLe();
        model.Stock = 0;

        // Hết hàng là trạng thái bình thường, khác hẳn giá bằng 0.
        Assert.False(CoLoiO(Validate(model), nameof(ProductFormViewModel.Stock)));
    }

    [Fact]
    public void Chua_chon_danh_muc_thi_bao_loi()
    {
        var model = TaoModelHopLe();
        model.CategoryId = 0;

        Assert.True(CoLoiO(Validate(model), nameof(ProductFormViewModel.CategoryId)));
    }

    [Fact]
    public void Data_Annotation_KHONG_phat_hien_duoc_danh_muc_khong_ton_tai()
    {
        var model = TaoModelHopLe();
        model.CategoryId = 999_999; // chắc chắn không có trong DB

        // Test này khoanh vùng GIỚI HẠN của Data Annotation, không phải khoanh
        // vùng một tính năng: attribute chỉ thấy con số, không thấy database.
        // Đây chính là lý do phép kiểm tra "danh mục có tồn tại" phải nằm ở
        // ProductService - xem ProductServiceTests.
        Assert.Empty(Validate(model));
    }
}
