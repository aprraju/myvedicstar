using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyVedicStar.Authorize.Data.Persistence.Entities;
using MyVedicStar.Authorize.Data.Users;

namespace MyVedicStar.Authorize.Data.Persistence.Configurations;

internal sealed class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public const string TableName = "User";
    public const string ExternalIdUniqueIndexName = "UX_User_Issuer_ExternalId";

    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.ToTable(TableName, AuthorizeDbContext.Schema);

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.ExternalId).HasMaxLength(UserProfileData.MaxExternalIdLength).IsRequired();
        builder.Property(user => user.Issuer).HasMaxLength(UserProfileData.MaxIssuerLength).IsRequired();
        builder.Property(user => user.IsActive).HasDefaultValue(true);
        builder.HasIndex(user => new { user.Issuer, user.ExternalId }).IsUnique().HasDatabaseName(ExternalIdUniqueIndexName);

        builder.Property(user => user.Email).HasMaxLength(UserProfileData.MaxEmailLength);
        builder.Property(user => user.Name).HasMaxLength(UserProfileData.MaxNameLength);

        builder.Property(user => user.CreatedUtc).HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(user => user.UpdatedUtc).HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
    }
}
