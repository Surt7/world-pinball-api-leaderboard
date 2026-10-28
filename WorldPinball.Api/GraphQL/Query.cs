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

public record ScoreStatsDto(int TotalGames, long BestScore);

public class Query
{
    // Fast aggregate query: COUNT(*) and MAX(score) computed directly by PostgreSQL
    public async Task<ScoreStatsDto> GetMyScoreStats(
        string? modeCode,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        ClaimsPrincipal? claimsPrincipal)
    {
        var principal = claimsPrincipal ?? httpContextAccessor.HttpContext?.User;
        var sub = principal?.FindFirst("sub")?.Value 
               ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(sub))
        {
            return new ScoreStatsDto(0, 0);
        }

        var query = db.ScoreEntries.AsNoTracking().Where(s => s.UserId == sub);

        if (!string.IsNullOrEmpty(modeCode) && modeCode != "all")
        {
            query = query.Where(s => s.Mode.Code == modeCode.ToLower());
        }

        var total = await query.CountAsync();
        var best = total > 0 ? await query.MaxAsync(s => s.Score) : 0;

        return new ScoreStatsDto(total, best);
    }

    // 1. Tab Scores (Personal scores of the authenticated user with server-side pagination)
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<ScoreEntry> GetMyScores(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        ClaimsPrincipal? claimsPrincipal,
        int skip = 0,
        int take = 20)
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
            .Skip(skip)
            .Take(take > 50 ? 50 : take);
    }

    // 2. Tab Leaderboard: global scores with pagination
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<ScoreEntry> GetScores(
        [Service] AppDbContext db,
        int skip = 0,
        int take = 50)
        => db.ScoreEntries
             .AsNoTracking()
             .Skip(skip)
             .Take(take > 100 ? 100 : take);

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
