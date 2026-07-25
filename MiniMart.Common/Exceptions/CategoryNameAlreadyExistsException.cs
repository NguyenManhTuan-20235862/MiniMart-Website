namespace MiniMart.Common.Exceptions;

public class CategoryNameAlreadyExistsException : Exception
{
    public string Name { get; }

    public CategoryNameAlreadyExistsException(string name)
        : base($"Danh mục '{name}' đã tồn tại.")
    {
        Name = name;
    }
}
