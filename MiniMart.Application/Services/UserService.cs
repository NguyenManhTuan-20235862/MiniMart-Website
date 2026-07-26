using Microsoft.AspNetCore.Identity;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher<User> _passwordHasher;

    // Chỉ nhận abstraction: IUserRepository nằm ở Domain, không phải
    // UserRepository ở Infrastructure. Đây là DIP trên thực tế.
    public UserService(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher<User> passwordHasher)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
    }

    public async Task<User> RegisterAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        // Kiểm tra sớm để báo lỗi thân thiện trong trường hợp thường gặp.
        // Vẫn còn khe TOCTOU giữa đây và SaveChanges - unique index ở DB là
        // chốt chặn cuối, Repository dịch lỗi đó thành cùng exception này.
        if (await _userRepository.ExistsByUsernameAsync(username, cancellationToken))
        {
            throw new UsernameAlreadyExistsException(username);
        }

        var user = new User
        {
            Username = username,
            Role = UserRole.Customer
        };

        // HashPassword nhận cả user vì một số cài đặt trộn thông tin user vào
        // hash. PasswordHasher mặc định không dùng, nhưng chữ ký giữ nguyên.
        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        await _userRepository.AddAsync(user, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            // UnitOfWork chỉ biết "có unique constraint bị vi phạm". Tại đây ta
            // vừa insert đúng một User, mà User chỉ có một unique index duy nhất
            // - nên nguyên nhân chắc chắn là username trùng.
            throw new UsernameAlreadyExistsException(username);
        }

        return user;
    }

    /// <summary>
    /// Hash của một mật khẩu không ai dùng, chỉ để "đốt" thời gian khi username
    /// không tồn tại. Tính một lần duy nhất cho cả tiến trình (Lazy + static).
    /// </summary>
    private static readonly Lazy<string> HashGia = new(() =>
        new PasswordHasher<User>().HashPassword(
            new User { Username = string.Empty, PasswordHash = string.Empty },
            "khong-bao-gio-la-mat-khau-that-cua-ai"));

    public async Task<User?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByUsernameAsync(username, cancellationToken);

        if (user is null)
        {
            // Vẫn verify một hash giả để thời gian phản hồi không tiết lộ
            // username có tồn tại hay không.
            //
            // Nếu return null ngay tại đây: username SAI trả lời sau ~1ms (chỉ
            // một query), username ĐÚNG mà sai mật khẩu trả lời sau ~100ms (PBKDF2
            // 100k vòng). Chênh hai bậc độ lớn, đo được qua mạng -> kẻ tấn công
            // dò được danh sách username có thật rồi mới tập trung phá mật khẩu.
            //
            // Thông báo lỗi ở Controller đã là thông báo CHUNG; bước này bịt kênh
            // rò rỉ còn lại là THỜI GIAN.
            _passwordHasher.VerifyHashedPassword(
                new User { Username = username, PasswordHash = HashGia.Value },
                HashGia.Value,
                password);

            return null;
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        // Microsoft nâng số vòng lặp ở phiên bản mới -> hash cũ vẫn đúng nhưng
        // đã lỗi thời. Đây là thời điểm duy nhất ta có mật khẩu thô trong tay,
        // nên tranh thủ nâng cấp hash mà người dùng không hề hay biết.
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return user;
    }
}
