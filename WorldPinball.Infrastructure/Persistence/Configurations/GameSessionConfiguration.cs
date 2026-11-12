using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldPinball.Domain.Entities;

namespace WorldPinball.Infrastructure.Persistence.Configurations
{
    public sealed class GameSessionConfiguration : IEntityTypeConfiguration<GameSession>
    {
        public void Configure(EntityTypeBuilder<GameSession> e)
        {
            // =========================
            // game_sessions
            // =========================
            e.ToTable("game_sessions");
            e.HasKey(x => x.SessionId);

            e.Property(x => x.MachineId).IsRequired();
            e.Property(x => x.DeviceId).IsRequired();
            e.Property(x => x.ModeId).IsRequired();
            e.Property(x => x.StartedAtUtc).IsRequired();
            e.Property(x => x.EndedAtUtc);

         
            e.Property(x => x.State)
             .HasConversion<string>()
             .HasMaxLength(20)
             .IsRequired();

            e.Property(x => x.Nonce)
             .IsRequired()
             .HasMaxLength(64);

            e.Property(x => x.ClientVersion)
             .HasMaxLength(50);

            e.HasIndex(x => x.Nonce).IsUnique();

            e.HasIndex(x => new { x.DeviceId, x.StartedAtUtc });
            e.HasIndex(x => new { x.MachineId, x.StartedAtUtc });
            e.HasIndex(x => new { x.ModeId, x.StartedAtUtc });

           
            e.HasOne(x => x.Device)
             .WithMany()
             .HasForeignKey(x => x.DeviceId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Machine)
             .WithMany()
             .HasForeignKey(x => x.MachineId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Mode)
             .WithMany()
             .HasForeignKey(x => x.ModeId)
             .OnDelete(DeleteBehavior.Restrict);

            e.Ignore(x => x.Slots);
        }
    }
}
