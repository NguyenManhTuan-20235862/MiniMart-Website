using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MiniMart.Web.Areas.Admin.Models;
using MiniMart.Web.Models;

namespace MiniMart.Tests;

/// <summary>
/// Hợp đồng của model sửa hàng loạt.
///
/// <para>
/// Chưa có endpoint nào nhận nó, nên bộ test này CHƯA kiểm được việc model binding
/// dựng đúng <c>List</c> từ form - việc đó cần một request thật và sẽ được khoá ở bước
/// sau. Cái kiểm được ngay bây giờ là những thứ vẫn đúng bất kể endpoint: tập property
/// (chống over-posting), kiểu dữ liệu, và các ràng buộc validation.
/// </para>
/// </summary>
public class ProductBulkUpdateModelTests
{
    // ───────────── Chống over-posting bằng cấu trúc ─────────────

    [Fact]
    public void Dong_sua_hang_loat_chi_BIND_duoc_dung_bon_truong()
    {
        var bindDuoc = typeof(ProductBulkUpdateDto)
            .GetProperties()
            .Where(p => p.GetCustomAttributes(typeof(BindNeverAttribute), inherit: true).Length == 0)
            .Select(p => p.Name)
            .Order()
            .ToArray();

        // ★ Test CẤU TRÚC. Tập property BIND ĐƯỢC chính là hàng rào chống over-posting:
        // không có Name/CategoryId/ImageUrl nên không tồn tại cách nào đổi tên sản phẩm
        // qua màn hình này, kể cả khi người gửi tự chế thêm trường vào form.
        //
        // Lọc theo [BindNever] chứ không đếm tất cả property: DTO được phép mang thêm
        // trường CHỈ-ĐỂ-HIỂN-THỊ (như Name), và những trường đó vô hại vì không có
        // đường đi ngược. Đếm tất cả sẽ chặn cả thứ vô hại và đẩy người sau sang một
        // giải pháp tệ hơn - ghép hai danh sách song song theo chỉ số.
        Assert.Equal(
            new[] { "Id", "Price", "RowVersion", "Stock" },
            bindDuoc);
    }

    [Fact]
    public void Truong_chi_de_hien_thi_phai_co_BindNever()
    {
        var name = typeof(ProductBulkUpdateDto).GetProperty(nameof(ProductBulkUpdateDto.Name))!;

        // Name có mặt trên đường server -> view, và KHÔNG được có đường ngược lại.
        Assert.NotEmpty(name.GetCustomAttributes(typeof(BindNeverAttribute), inherit: true));
    }

    [Fact]
    public void RowVersion_phai_la_byte_array()
    {
        var kieu = typeof(ProductBulkUpdateDto)
            .GetProperty(nameof(ProductBulkUpdateDto.RowVersion))!
            .PropertyType;

        // Model binder mặc định giải mã Base64 -> byte[]. Đổi sang string thì binding
        // vẫn chạy nhưng Service nhận một chuỗi không so sánh được với cột rowversion,
        // và Optimistic Concurrency biến mất trong im lặng.
        Assert.Equal(typeof(byte[]), kieu);
    }

    [Fact]
    public void TongSoTrang_khong_duoc_bind_tu_form()
    {
        var thuocTinh = typeof(ProductBulkUpdateViewModel)
            .GetProperty(nameof(ProductBulkUpdateViewModel.TongSoTrang))!;

        // Kết luận của server, không phải dữ liệu client tự khai.
        Assert.NotNull(
            thuocTinh.GetCustomAttributes(typeof(BindNeverAttribute), inherit: true)
                .FirstOrDefault());
    }

    [Fact]
    public void Items_mac_dinh_la_danh_sach_RONG_khong_phai_null()
    {
        var model = new ProductBulkUpdateViewModel();

        // POST một form không có dòng nào (bảng rỗng, hoặc JS gỡ hết dòng) thì binder
        // không gán gì cho Items. Để null thì Controller ném NullReference ngay dòng
        // đầu; danh sách rỗng cho ra "lưu 0 dòng" - đúng nghĩa và không nổ.
        Assert.NotNull(model.Items);
        Assert.Empty(model.Items);
        Assert.True(model.KhongCoDong);
    }

    // ───────────── Ràng buộc trên từng dòng ─────────────

    [Theory]
    [InlineData(0.01, 0, true)]
    [InlineData(1000, 5, true)]
    [InlineData(0, 5, false)]        // giá 0 không hợp lệ
    [InlineData(-1, 5, false)]
    [InlineData(1000, -1, false)]    // tồn kho âm
    public void Gia_va_ton_kho_phai_theo_dung_rang_buoc(decimal gia, int ton, bool hopLe)
    {
        var loi = KiemTra(new ProductBulkUpdateDto { Id = 1, Price = gia, Stock = ton });

        Assert.Equal(hopLe, loi.Count == 0);
    }

    [Fact]
    public void Rang_buoc_gia_giong_HET_form_sua_le()
    {
        // Hai màn hình cùng ghi vào một cột mà đặt hai giới hạn khác nhau thì cột đó
        // thực chất không có giới hạn nào - kẻ muốn lách chỉ cần chọn màn hình dễ hơn.
        Assert.Equal(
            LayRange(typeof(ProductFormViewModel), nameof(ProductFormViewModel.Price)),
            LayRange(typeof(ProductBulkUpdateDto), nameof(ProductBulkUpdateDto.Price)));

        Assert.Equal(
            LayRange(typeof(ProductFormViewModel), nameof(ProductFormViewModel.Stock)),
            LayRange(typeof(ProductBulkUpdateDto), nameof(ProductBulkUpdateDto.Stock)));
    }

    /// <summary>
    /// MỌI cột tiền trong dự án - test này tìm ra một lỗi thật đang tồn tại.
    ///
    /// <para>
    /// <c>[Range(typeof(decimal), "min", "max")]</c> có HAI cờ culture khác nhau, và
    /// chúng chi phối hai việc khác nhau:
    /// </para>
    /// <list type="bullet">
    /// <item><c>ParseLimitsInInvariantCulture</c> — parse hai chuỗi CẬN.</item>
    /// <item><c>ConvertValueInInvariantCulture</c> — chuyển đổi GIÁ TRỊ đang được kiểm.</item>
    /// </list>
    /// <para>
    /// Quy ước cũ của dự án chỉ đặt cờ thứ hai. Đã đo trực tiếp dưới vi-VN: chỉ
    /// <c>ConvertValue</c> thì <c>IsValid</c> <b>ném ArgumentException</b> (không phải
    /// "parse ra số khác" như tài liệu cũ viết), tức form Admin trả HTTP 500 trên máy
    /// vi-VN. Chỉ <c>ParseLimits</c> thì chạy đúng.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(typeof(ProductBulkUpdateDto), nameof(ProductBulkUpdateDto.Price))]
    [InlineData(typeof(ProductFormViewModel), nameof(ProductFormViewModel.Price))]
    [InlineData(typeof(ProductFilter), nameof(ProductFilter.MinPrice))]
    [InlineData(typeof(ProductFilter), nameof(ProductFilter.MaxPrice))]
    public void Moi_cot_tien_phai_parse_can_theo_InvariantCulture(Type kieu, string thuocTinh)
    {
        var range = LayRangeAttribute(kieu, thuocTinh);

        // Khẳng định thẳng vào thuộc tính: không có gì cache được, và không phụ thuộc
        // culture của máy đang chạy test.
        Assert.True(
            range.ParseLimitsInInvariantCulture,
            $"{kieu.Name}.{thuocTinh} thiếu ParseLimitsInInvariantCulture. "
            + "Trên máy locale vi-VN, chuỗi cận \"0.01\" ném ArgumentException ngay "
            + "trong lúc validate -> HTTP 500. Máy dev en-US không tái hiện được.");

        Assert.True(range.ConvertValueInInvariantCulture);
    }

    [Theory]
    [InlineData(typeof(ProductBulkUpdateDto), nameof(ProductBulkUpdateDto.Price))]
    [InlineData(typeof(ProductFormViewModel), nameof(ProductFormViewModel.Price))]
    public void Can_duoi_cua_gia_KHONG_doi_theo_locale_may_chay(Type kieu, string thuocTinh)
    {
        var goc = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("vi-VN");

            // Lấy instance MỚI rồi mới gọi IsValid lần đầu: RangeAttribute cache phép
            // chuyển đổi ở lần gọi đầu tiên, nên dùng lại một instance đã chạy dưới
            // culture khác là đo nhầm. Chính cái bẫy đó làm bản đầu của test này lọt lưới.
            var range = LayRangeAttribute(kieu, thuocTinh);

            Assert.True(range.IsValid(0.5m), "0,5 đồng phải hợp lệ (cận dưới là 0.01).");
            Assert.False(range.IsValid(0m));
        }
        finally
        {
            CultureInfo.CurrentCulture = goc;
        }
    }

    [Fact]
    public void Thieu_RowVersion_KHONG_lam_dong_do_validation()
    {
        var loi = KiemTra(new ProductBulkUpdateDto
        {
            Id = 1,
            Price = 1000m,
            Stock = 5,
            RowVersion = null
        });

        // null nghĩa là "bỏ qua kiểm tra phiên bản" - dành cho luồng nội bộ không có
        // form. Việc đòi RowVersion cho đường ĐI QUA FORM là trách nhiệm của Controller
        // và Service, không phải của Data Annotation: annotation không phân biệt được
        // request đến từ đâu.
        Assert.Empty(loi);
    }

    // ───────────── Helper ─────────────

    private static List<ValidationResult> KiemTra(object model)
    {
        var ketQua = new List<ValidationResult>();

        Validator.TryValidateObject(
            model, new ValidationContext(model), ketQua, validateAllProperties: true);

        return ketQua;
    }

    /// <summary>
    /// Lấy <c>[Range]</c> khai báo trên một property, LUÔN là instance mới.
    ///
    /// <para>
    /// "Instance mới" là điều kiện bắt buộc cho test culture: <c>RangeAttribute</c>
    /// cache phép chuyển đổi <c>string -&gt; decimal</c> ở lần <c>IsValid</c> đầu tiên,
    /// nên dùng lại một instance đã chạy dưới culture khác là đo nhầm.
    /// </para>
    /// </summary>
    private static RangeAttribute LayRangeAttribute(Type kieu, string tenThuocTinh) =>
        kieu.GetProperty(tenThuocTinh)!
            .GetCustomAttributes(typeof(RangeAttribute), inherit: true)
            .Cast<RangeAttribute>()
            .Single();

    /// <summary>Đọc cận dưới/cận trên của <c>[Range]</c> để so hai model với nhau.</summary>
    private static (object? Min, object? Max) LayRange(Type kieu, string tenThuocTinh)
    {
        var range = LayRangeAttribute(kieu, tenThuocTinh);

        return (range.Minimum, range.Maximum);
    }
}
