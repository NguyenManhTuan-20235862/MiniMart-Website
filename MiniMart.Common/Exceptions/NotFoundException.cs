namespace MiniMart.Common.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string entityName, object key)
        : base($"Không tìm thấy {entityName} với mã '{key}'.")
    {
    }
}
