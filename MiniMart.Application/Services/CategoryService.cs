using MiniMart.Application.Interfaces;
using MiniMart.Common.Exceptions;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;

namespace MiniMart.Application.Services;

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CategoryService(ICategoryRepository categoryRepository, IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public Task<List<Category>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _categoryRepository.GetAllAsync(cancellationToken);

    public Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _categoryRepository.GetByIdAsync(id, cancellationToken);

    public async Task<Category> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();

        if (await _categoryRepository.ExistsByNameAsync(name, cancellationToken: cancellationToken))
        {
            throw new CategoryNameAlreadyExistsException(name);
        }

        var category = new Category { Name = name };

        await _categoryRepository.AddAsync(category, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            // Khe TOCTOU: request khác chèn cùng tên sau khi ta kiểm tra xong.
            // Unique index ở DB chặn lại, ta dịch về đúng lỗi nghiệp vụ.
            throw new CategoryNameAlreadyExistsException(name);
        }

        return category;
    }

    public async Task UpdateAsync(int id, string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();

        var category = await _categoryRepository.GetForUpdateAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Category), id);

        // excludeId: giữ nguyên tên cũ khi sửa thì không tính là trùng với chính nó.
        if (await _categoryRepository.ExistsByNameAsync(name, excludeId: id, cancellationToken))
        {
            throw new CategoryNameAlreadyExistsException(name);
        }

        category.Name = name;

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            throw new CategoryNameAlreadyExistsException(name);
        }
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await _categoryRepository.GetForUpdateAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Category), id);

        // ─── QUY TẮC NGHIỆP VỤ ───
        // Chặn ở đây để người dùng nhận thông báo dễ hiểu. Ràng buộc FK
        // Restrict dưới DB vẫn là bảo đảm cuối cùng cho trường hợp có request
        // khác thêm sản phẩm vào danh mục ngay sau lệnh kiểm tra này.
        if (await _categoryRepository.HasProductsAsync(id, cancellationToken))
        {
            throw new CategoryHasProductsException(id);
        }

        _categoryRepository.Remove(category);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
