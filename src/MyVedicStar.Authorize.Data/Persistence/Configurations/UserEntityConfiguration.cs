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
        builder.Property(user => user.GivenName).HasColumnName("given_name").HasMaxLength(UserProfileData.MaxNameLength);
        builder.Property(user => user.FamilyName).HasColumnName("family_name").HasMaxLength(UserProfileData.MaxNameLength);

        builder.Property(user => user.StreetAddress).HasMaxLength(256);
        builder.Property(user => user.StreetAddress2).HasMaxLength(256);
        builder.Property(user => user.Country).HasMaxLength(256);
        builder.Property(user => user.State).HasMaxLength(256);
        builder.Property(user => user.City).HasMaxLength(256);
        builder.Property(user => user.PostalCode).HasMaxLength(20);
        builder.Property(user => user.PhoneNumber).HasMaxLength(40);
        builder.Property(user => user.ProfileRevision).IsConcurrencyToken().HasDefaultValue(0L);
        builder.Property(user => user.ProfileManaged).HasDefaultValue(false);
        builder.Property(user => user.FirstNameSyncPending).HasDefaultValue(false);
        builder.Property(user => user.LastNameSyncPending).HasDefaultValue(false);

        builder.Property(user => user.CreatedUtc).HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(user => user.UpdatedUtc).HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
    }
}
