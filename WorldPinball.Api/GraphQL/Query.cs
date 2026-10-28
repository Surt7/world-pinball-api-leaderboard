using System.Security.Claims;
using Microsoft.AspNetCore.Http;
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
    // 1. Tab Scores (Personal scores of the authenticated user)
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<ScoreEntry> GetMyScores(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        ClaimsPrincipal? claimsPrincipal)
    {
        var principal = claimsPrincipal ?? httpContextAccessor.HttpContext?.User;
        var sub = principal?.FindFirst("sub")?.Value 
               ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(sub))
        {
            return Enumerable.Empty<ScoreEntry>().AsQueryable();
        }

        return db.ScoreEntries
            .AsNoTracking()
            .Where(s => s.UserId == sub)
            .Take(50);
    }

    // 2. Tab Leaderboard: global scores capped at 50 items
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<ScoreEntry> GetScores([Service] AppDbContext db)
        => db.ScoreEntries.AsNoTracking().Take(50);

    // 2. Tab Machines (Option A): returns only the active machines of the authenticated user's devices
    // User -> Device (OwnerUserId) -> MachineDevice (IsActive) -> Machine
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Machine> GetMachines(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        ClaimsPrincipal? claimsPrincipal)
    {
        var principal = claimsPrincipal ?? httpContextAccessor.HttpContext?.User;
        var sub = principal?.FindFirst("sub")?.Value 
               ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (Guid.TryParse(sub, out var userId))
        {
            return db.Devices
                .AsNoTracking()
                .Where(d => d.OwnerUserId == userId)
                .SelectMany(d => d.MachineDevices.Where(md => md.IsActive).Select(md => md.Machine))
                .Take(50);
        }

        // When not authenticated, user owns 0 machines
        return Enumerable.Empty<Machine>().AsQueryable();
    }

    // 3. Tab Profil: returns the profile of the authenticated user
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<UserProfile> GetProfiles(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        ClaimsPrincipal? claimsPrincipal)
    {
        var principal = claimsPrincipal ?? httpContextAccessor.HttpContext?.User;
        var sub = principal?.FindFirst("sub")?.Value 
               ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (Guid.TryParse(sub, out var userId))
        {
            return db.Set<UserProfile>().AsNoTracking().Where(p => p.UserId == userId);
        }

        return db.Set<UserProfile>().AsNoTracking().Where(p => p.IsProfilePublic).Take(50);
    }
}
