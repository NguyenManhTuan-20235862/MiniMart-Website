using Microsoft.AspNetCore.Identity;
using MiniMart.Application.Services;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using Moq;

namespace MiniMart.Tests;

/// <summary>
/// Test nghiệp vụ đăng ký/đăng nhập KHÔNG cần database.
/// Làm được điều này chính là phần thưởng của DIP: UserService phụ thuộc
/// IUserRepository (abstraction ở Domain), nên ta thay nó bằng mock được.
/// Nếu Service gọi thẳng UserRepository thì mọi test dưới đây đều phải có
/// SQL Server chạy kèm.
/// PasswordHasher dùng bản THẬT, vì chính hành vi hash là thứ cần kiểm chứng.
/// </summary>
public class UserServiceTests
{
    private readonly Mock<IUserRepository> _repository = new();
    private readonly IPasswordHasher<User> _hasher = new PasswordHasher<User>();

    private UserService CreateSut() => new(_repository.Object, _hasher);

    private void GiaSuUsernameChuaTonTai() =>
        _repository
            .Setup(r => r.ExistsByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

    [Fact]
    public async Task RegisterAsync_khong_duoc_luu_mat_khau_dang_tho()
    {
        GiaSuUsernameChuaTonTai();
        var sut = CreateSut();

        var user = await sut.RegisterAsync("tuan", "MatKhau123");

        // Lỗi nghiêm trọng nhất có thể xảy ra ở tầng này.
        Assert.NotEqual("MatKhau123", user.PasswordHash);
        Assert.DoesNotContain("MatKhau123", user.PasswordHash);
    }

    [Fact]
    public async Task RegisterAsync_hai_nguoi_cung_mat_khau_phai_ra_hai_hash_khac_nhau()
    {
        GiaSuUsernameChuaTonTai();
        var sut = CreateSut();

        var a = await sut.RegisterAsync("nguoi_a", "MatKhauGiongNhau");
        var b = await sut.RegisterAsync("nguoi_b", "MatKhauGiongNhau");

        // Đây là bằng chứng SALT đang hoạt động. Không có salt thì hai hash
        // trùng nhau, và một bảng tra cứu dựng sẵn sẽ phá được cả hai cùng lúc.
        Assert.NotEqual(a.PasswordHash, b.PasswordHash);
    }

    [Fact]
    public async Task RegisterAsync_username_da_ton_tai_thi_nem_exception()
    {
        _repository
            .Setup(r => r.ExistsByUsernameAsync("tuan", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<UsernameAlreadyExistsException>(
            () => sut.RegisterAsync("tuan", "MatKhau123"));

        Assert.Equal("tuan", ex.Username);

        // Đã chặn từ sớm thì không được đụng tới DB.
        _repository.Verify(
            r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_mac_dinh_phai_la_Customer_khong_phai_Admin()
    {
        GiaSuUsernameChuaTonTai();
        var sut = CreateSut();

        var user = await sut.RegisterAsync("tuan", "MatKhau123");

        // Sai chỗ này là mọi người đăng ký đều thành quản trị viên.
        Assert.Equal(UserRole.Customer, user.Role);
    }

    [Fact]
    public async Task RegisterAsync_phai_goi_SaveChanges_dung_mot_lan()
    {
        GiaSuUsernameChuaTonTai();
        var sut = CreateSut();

        await sut.RegisterAsync("tuan", "MatKhau123");

        _repository.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_khong_tim_thay_user_thi_tra_ve_null()
    {
        _repository
            .Setup(r => r.GetByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var sut = CreateSut();

        var result = await sut.AuthenticateAsync("khong_ton_tai", "MatKhau123");

        Assert.Null(result);
    }

    [Fact]
    public async Task AuthenticateAsync_sai_mat_khau_thi_tra_ve_null()
    {
        var sut = CreateSut();
        var user = TaoUserVoiMatKhau("tuan", "MatKhauDung");
        _repository
            .Setup(r => r.GetByUsernameAsync("tuan", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await sut.AuthenticateAsync("tuan", "MatKhauSai");

        Assert.Null(result);
    }

    [Fact]
    public async Task AuthenticateAsync_dung_mat_khau_thi_tra_ve_user()
    {
        var sut = CreateSut();
        var user = TaoUserVoiMatKhau("tuan", "MatKhauDung");
        _repository
            .Setup(r => r.GetByUsernameAsync("tuan", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await sut.AuthenticateAsync("tuan", "MatKhauDung");

        Assert.NotNull(result);
        Assert.Equal("tuan", result!.Username);
    }

    private User TaoUserVoiMatKhau(string username, string password)
    {
        var user = new User { Id = 1, Username = username, Role = UserRole.Customer };
        user.PasswordHash = _hasher.HashPassword(user, password);
        return user;
    }
}
