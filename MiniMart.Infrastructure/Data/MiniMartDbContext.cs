using Microsoft.EntityFrameworkCore;
using MiniMart.Domain.Entities;

namespace MiniMart.Infrastructure.Data;

public class MiniMartDbContext : DbContext
{
    // Constructor nhận DbContextOptions là cách AddDbContext truyền provider
    // và connection string vào. Không tự mở connection trong class này.
    public MiniMartDbContext(DbContextOptions<MiniMartDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    // Phase 2: thêm DbSet<Product>, DbSet<Order>... tại đây

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tự nạp mọi IEntityTypeConfiguration<T> trong assembly này, để cấu hình
        // Fluent API của từng entity nằm ở file riêng thay vì dồn hết vào đây.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiniMartDbContext).Assembly);
    }
}
