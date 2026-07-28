using Microsoft.EntityFrameworkCore;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Interfaces;
using MiniMart.Infrastructure.Data;

namespace MiniMart.Infrastructure.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly MiniMartDbContext _context;

    public PaymentRepository(MiniMartDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        await _context.Payments.AddAsync(payment, cancellationToken);
    }

    public async Task<Payment?> GetByOrderIdAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        // Đường ĐỌC -> AsNoTracking.
        return await _context.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
    }
}
