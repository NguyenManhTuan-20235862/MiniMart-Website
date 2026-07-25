using Microsoft.AspNetCore.Identity;
using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher<User> _passwordHasher;

    // Chỉ nhận abstraction: IUserRepository nằm ở Domain, không phải
    // UserRepository ở Infrastructure. Đây là DIP trên thực tế.
    public UserService(IUserRepository userRepository, IPasswordHasher<User> passwordHasher)
    {
        _userRepository = userRepository;
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
        await _userRepository.SaveChangesAsync(cancellationToken);

        return user;
    }

    public async Task<User?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByUsernameAsync(username, cancellationToken);

        if (user is null)
        {
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
            await _userRepository.SaveChangesAsync(cancellationToken);
        }

        return user;
    }
}
