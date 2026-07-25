using Microsoft.EntityFrameworkCore;

namespace MiniMart.Infrastructure.Data;

public class MiniMartDbContext : DbContext
{
    // Constructor nhận DbContextOptions là cách AddDbContext truyền provider
    // và connection string vào. Không tự mở connection trong class này.
    public MiniMartDbContext(DbContextOptions<MiniMartDbContext> options)
        : base(options)
    {
    }

    // Phase 2: khai báo DbSet tại đây
    // public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tự nạp mọi IEntityTypeConfiguration<T> trong assembly này, để cấu hình
        // Fluent API của từng entity nằm ở file riêng thay vì dồn hết vào đây.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiniMartDbContext).Assembly);
    }
}
