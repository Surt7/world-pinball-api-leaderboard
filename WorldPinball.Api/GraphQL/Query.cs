using Microsoft.EntityFrameworkCore;
using WorldPinball.Domain.Entities;
using WorldPinball.Infrastructure.Persistence;

namespace WorldPinball.Api.GraphQL;

// HotChocolate type configuration: ignores internal DomainEvents and internal relations
public class ScoreEntryType : ObjectType<ScoreEntry>
{
    protected override void Configure(IObjectTypeDescriptor<ScoreEntry> descriptor)
    {
        descriptor.Ignore(s => s.DomainEvents);
        descriptor.Ignore(s => s.Session);
    }
}

// Security: Ignore internal nonces, idempotency keys, device pairings and enrollment tokens
public class MachineType : ObjectType<Machine>
{
    protected override void Configure(IObjectTypeDescriptor<Machine> descriptor)
    {
        descriptor.Ignore(m => m.Nonce);
        descriptor.Ignore(m => m.IdempotencyKey);
        descriptor.Ignore(m => m.MachineDevices);
        descriptor.Ignore(m => m.EnrollmentTokens);
    }
}

public class UserProfileType : ObjectType<UserProfile>
{
    protected override void Configure(IObjectTypeDescriptor<UserProfile> descriptor)
    {
        // Publicly exposed fields only
    }
}

public class Query
{
    // 1. Tab Scores & Leaderboard: capped at 50 items (Top 50 / recent 50)
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<ScoreEntry> GetScores([Service] AppDbContext db)
        => db.ScoreEntries.AsNoTracking().Take(50);

    // 2. Tab Machines: capped at 100 items to prevent unbounded dumps
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Machine> GetMachines([Service] AppDbContext db)
        => db.Machines.AsNoTracking().Take(100);

    // 3. Tab Profil: capped at 50 items
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<UserProfile> GetProfiles([Service] AppDbContext db)
        => db.Set<UserProfile>().AsNoTracking().Take(50);
}
