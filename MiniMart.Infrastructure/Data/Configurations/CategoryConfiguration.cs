using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniMart.Domain.Entities;

namespace MiniMart.Infrastructure.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        // Hai danh mục trùng tên gây nhầm lẫn khi chọn lúc thêm sản phẩm.
        builder.HasIndex(c => c.Name)
            .IsUnique();
    }
}
