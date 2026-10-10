using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyVedicStar.Authorize.Data.Persistence.Entities;

namespace MyVedicStar.Authorize.Data.Persistence.Configurations;

internal sealed class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public const string TableName = "User";
    public const string ExternalIdUniqueIndexName = "UX_User_ExternalId";

    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.ToTable(TableName, AuthorizeDbContext.Schema);

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.ExternalId).HasMaxLength(128).IsRequired();
        builder.HasIndex(user => user.ExternalId).IsUnique().HasDatabaseName(ExternalIdUniqueIndexName);

        builder.Property(user => user.Email).HasMaxLength(320);
        builder.Property(user => user.Name).HasMaxLength(256);

        builder.Property(user => user.CreatedUtc).HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(user => user.UpdatedUtc).HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
    }
}
