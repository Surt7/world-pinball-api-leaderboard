using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldPinball.Domain.Entities;

namespace WorldPinball.Infrastructure.Persistence.Configurations
{
    public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
    {
        public void Configure(EntityTypeBuilder<Device> e)
        {

            // =========================
            // devices
            // =========================

            e.ToTable("devices");
            e.HasKey(x => x.DeviceId);

            e.Property(x => x.OwnerUserId);
            e.HasIndex(x => x.OwnerUserId);

            e.Property(x => x.SerialPhysical).IsRequired();

            // status varchar (UML)
            e.Property(x => x.Status)
                 .HasConversion<string>()
                 .HasMaxLength(20)
                 .IsRequired();

            e.Property(x => x.PublicKey);
            e.Property(x => x.PublicKeyFingerprint);
            e.Property(x => x.KeyType);
            e.Property(x => x.KeyAlgorithm);

            e.Property(x => x.LastCounter).IsRequired();

            e.Property(x => x.ProvisionedAtUtc);
            e.Property(x => x.ActivatedAtUtc);
            e.Property(x => x.RevokedAtUtc);
            e.Property(x => x.LastSeenAtUtc);

            e.Property(x => x.ClientVersion);
            e.Property(x => x.Notes);

            e.Property(x => x.CreatedAtUtc).IsRequired();
            e.Property(x => x.UpdatedAtUtc);

            e.HasIndex(x => x.SerialPhysical).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.LastSeenAtUtc);
            e.HasIndex(x => x.PublicKeyFingerprint);

        }
    }
}
