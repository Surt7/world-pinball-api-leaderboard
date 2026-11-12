using System.Security.Cryptography;
using System.Text;
using Bogus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorldPinball.Domain.Entities;
using WorldPinball.Domain.Enums;
using Microsoft.AspNetCore.WebUtilities;
using WorldPinball.Infrastructure.Persistence.Entities;

namespace WorldPinball.Infrastructure.Persistence.Seed;

public static class DevelopmentDataSeeder
{
    private const int Seed = 20260903;

    private const string DevelopmentUserId = "development-seeder";
    public const string DevOperatorToken = "wpt_dev_9f8e7d6c5b4a32109876543210fedcba";

    //event below are for seq log
    //seed start event
    private static readonly EventId SeedStarted =
        new(1000, nameof(SeedStarted));

 
    private static readonly EventId SeedCompleted =
        new(1001, nameof(SeedCompleted));

 
    private static readonly EventId SeedFailed =
        new(1002, nameof(SeedFailed));

    public static async Task SeedAsync(
        AppDbContext context,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            SeedStarted,
            "Development database seeding started.");
        
        try
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var publishers = await context.Publishers.ToListAsync(cancellationToken);
            if (publishers.Count == 0)
            {
                publishers = CreatePublishers(now);
                await context.Publishers.AddRangeAsync(publishers, cancellationToken);
            }

            var regions = await context.Regions.ToListAsync(cancellationToken);
            if (regions.Count == 0)
            {
                regions = CreateRegions(now);
                await context.Regions.AddRangeAsync(regions, cancellationToken);
            }

            var gameModes = await context.GameModes.ToListAsync(cancellationToken);
            if (gameModes.Count == 0)
            {
                gameModes = CreateGameModes(now);
                await context.GameModes.AddRangeAsync(gameModes, cancellationToken);
            }

            var ipLicenses = await context.IpLicenses
                .Include(x => x.Regions)
                .ToListAsync(cancellationToken);
            if (ipLicenses.Count == 0)
            {
                ipLicenses = CreateIpLicenses(publishers, regions, now);
                await context.IpLicenses.AddRangeAsync(ipLicenses, cancellationToken);
            }
            else
            {
                AddMissingRegionLinks(ipLicenses, regions);
            }

            var machines = await context.Machines.ToListAsync(cancellationToken);
            if (machines.Count == 0)
            {
                machines = CreateMachines(ipLicenses, now);
                await context.Machines.AddRangeAsync(machines, cancellationToken);
            }

            var devices = await context.Devices.ToListAsync(cancellationToken);
            if (devices.Count == 0)
            {
                devices = CreateDevices(now);
                if (devices.Count > 0)
                {
                    devices[0].AssignOwner(Guid.Parse("a2c66552-2b07-42cb-9814-0f5abacfd902"));
                }
                if (devices.Count > 1)
                {
                    devices[1].AssignOwner(Guid.Parse("a2c66552-2b07-42cb-9814-0f5abacfd902"));
                }
                await context.Devices.AddRangeAsync(devices, cancellationToken);
            }

            var machineDevices = await context.MachineDevices.ToListAsync(cancellationToken);
            if (machineDevices.Count == 0)
            {
                machineDevices = CreateMachineDevices(machines, devices, now);
                await context.MachineDevices.AddRangeAsync(machineDevices, cancellationToken);
            }

            var caCertificates = await context.CaCertificates.ToListAsync(cancellationToken);
            if (caCertificates.Count == 0)
            {
                caCertificates = CreateCaCertificates(now);
                await context.CaCertificates.AddRangeAsync(caCertificates, cancellationToken);
            }

            var deviceCertificates = await context.DeviceCertificates.ToListAsync(cancellationToken);
            if (deviceCertificates.Count == 0)
            {
                deviceCertificates = CreateDeviceCertificates(devices, caCertificates);
                await context.DeviceCertificates.AddRangeAsync(deviceCertificates, cancellationToken);
            }

            if (!await context.EnrollmentTokens.AnyAsync(cancellationToken))
            {
                var enrollmentTokens = CreateEnrollmentTokens(machines, devices, now);
                await context.EnrollmentTokens.AddRangeAsync(enrollmentTokens, cancellationToken);
            }

            if (!await context.OperatorTokens.AnyAsync(cancellationToken))
            {
                var devOperatorToken = new OperatorToken(
                    name: "Local Dev Operator",
                    rawToken: DevOperatorToken,
                    scopes: "*",
                    issuedByUserId: DevelopmentUserId,
                    nowUtc: now);
                await context.OperatorTokens.AddAsync(devOperatorToken, cancellationToken);
            }

            var missions = await context.Missions.ToListAsync(cancellationToken);
            if (missions.Count == 0)
            {
                missions = CreateMissions(ipLicenses, gameModes, now);
                await context.Missions.AddRangeAsync(missions, cancellationToken);
            }

            var gameSessions = await context.GameSessions.ToListAsync(cancellationToken);
            if (gameSessions.Count == 0)
            {
                gameSessions = CreateGameSessions(machineDevices, gameModes, now);
                await context.GameSessions.AddRangeAsync(gameSessions, cancellationToken);
            }

            if (!await context.ScoreEntries.AnyAsync(cancellationToken))
            {
                var scoreEntries = CreateScoreEntries(gameSessions, machines, now);
                await context.ScoreEntries.AddRangeAsync(scoreEntries, cancellationToken);
            }

            if (!await context.MissionProgress.AnyAsync(cancellationToken))
            {
                var missionProgress = CreateMissionProgress(missions, now);
                await context.MissionProgress.AddRangeAsync(missionProgress, cancellationToken);
            }

            if (!await context.CertificateRevocations.AnyAsync(cancellationToken))
            {
                var revocations = CreateCertificateRevocations(deviceCertificates, now);
                await context.CertificateRevocations.AddRangeAsync(revocations, cancellationToken);
            }
            
            if (!await context.RolePermissions.AnyAsync(cancellationToken))
            {
                var defaultRolePermissions = CreateRolePermissions();
                await context.RolePermissions.AddRangeAsync(defaultRolePermissions, cancellationToken);
            }

            if (!await context.Set<UserProfile>().AnyAsync(cancellationToken))
            {
                var player1Profile = new UserProfile(
                    Guid.Parse("a2c66552-2b07-42cb-9814-0f5abacfd902"),
                    displayName: "Philippe",
                    initials: "PHI",
                    avatarUrl: string.Empty,
                    countryCode: "FR",
                    isProfilePublic: true,
                    createdAtUtc: now);
                await context.Set<UserProfile>().AddAsync(player1Profile, cancellationToken);
            }
            
            foreach (var machine in context.ChangeTracker
                         .Entries<Machine>()
                         .Select(x => x.Entity))
            {
                Console.WriteLine(
                    $"Nonce={machine.Nonce?.Length} | " +
                    $"Manufacturer={machine.Manufacturer?.Length} | " +
                    $"Model={machine.ManufacturerModelName?.Length}");
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            
            logger.LogInformation(
                SeedCompleted,
                "Development database seeding completed successfully.");
        }
        catch (Exception exception)
        {
            logger.LogError(
                SeedFailed,
                exception,
                "Development database seeding failed.");
            throw;
        }
    }

    private static void AddMissingRegionLinks(
        IReadOnlyList<IpLicense> licenses,
        IReadOnlyList<Region> regions)
    {
        if (regions.Count == 0)
            return;

        for (var index = 0; index < licenses.Count; index++)
        {
            if (licenses[index].Regions.Count != 0)
                continue;

            licenses[index].Regions.Add(regions[index % regions.Count]);

            if (regions.Count > 1)
                licenses[index].Regions.Add(regions[(index + 1) % regions.Count]);
        }
    }

    private static List<Publisher> CreatePublishers(DateTime now) =>
        new Faker<Publisher>("fr").UseSeed(Seed + 1)
            .CustomInstantiator(f => new Publisher(
                $"{f.Company.CompanyName()} {f.IndexFaker + 1}",
                $"publisher-{f.IndexFaker + 1}",
                now))
            .Generate(5);

    private static List<Region> CreateRegions(DateTime now) =>
        new Faker<Region>("fr").UseSeed(Seed + 2)
            .CustomInstantiator(f => new Region(
                $"R{f.IndexFaker + 1:00}",
                $"{f.Address.Country()} {f.IndexFaker + 1}",
                now))
            .Generate(6);

    private static List<GameMode> CreateGameModes(DateTime now) =>
        new Faker<GameMode>("fr").UseSeed(Seed + 3)
            .CustomInstantiator(f => new GameMode(
                $"mode-{f.IndexFaker + 1}",
                $"Mode {f.Hacker.Noun()} {f.IndexFaker + 1}",
                f.Lorem.Sentence(),
                f.IndexFaker != 4,
                now))
            .Generate(5);

    private static List<IpLicense> CreateIpLicenses(
        IReadOnlyList<Publisher> publishers,
        IReadOnlyList<Region> regions,
        DateTime now)
    {
        var licenses = new Faker<IpLicense>("fr").UseSeed(Seed + 4)
            .CustomInstantiator(f => new IpLicense(
                $"{f.Commerce.ProductName()} {f.IndexFaker + 1}",
                $"ip-license-{f.IndexFaker + 1}",
                publishers[f.Random.Int(0, publishers.Count - 1)].PublisherId,
                f.Company.CompanyName(),
                f.Date.Past(40, now).Year,
                f.Lorem.Sentence(),
                f.IndexFaker != 9,
                now))
            .Generate(10);

        for (var index = 0; index < licenses.Count; index++)
        {
            licenses[index].Regions.Add(regions[index % regions.Count]);
            licenses[index].Regions.Add(regions[(index + 1) % regions.Count]);
        }

        return licenses;
    }

    private static List<Machine> CreateMachines(
        IReadOnlyList<IpLicense> licenses,
        DateTime now) =>
        new Faker<Machine>("fr").UseSeed(Seed + 5)
            .CustomInstantiator(f => new Machine(
                licenses[f.Random.Int(0, licenses.Count - 1)].IpId,
                $"WP-{f.IndexFaker + 1:00000}",
                $"Flipper {f.Commerce.ProductAdjective()} {f.IndexFaker + 1}",
                Utc(f.Date.Past(2, now)),
                f.Random.Guid(),
                WebEncoders.Base64UrlEncode(f.Random.Bytes(32))))
            .RuleFor(x => x.Manufacturer, f => f.Company.CompanyName())
            .RuleFor(x => x.ManufacturerModelCode, f => $"MODEL-{f.IndexFaker + 1:000}")
            .RuleFor(x => x.ManufacturerModelName, f => f.Commerce.ProductName())
            .RuleFor(x => x.ManufacturerModelYear, f => f.Date.Past(15, now).Year.ToString())
            .RuleFor(x => x.ManufacturerSerialNumber, f => $"MFG-{f.Random.AlphaNumeric(12).ToUpperInvariant()}")
            .RuleFor(x => x.ManufacturerNotes, f => f.Lorem.Sentence())
            .RuleFor(x => x.MachineNotes, f => f.Lorem.Sentence())
            .RuleFor(x => x.Location, f => $"{f.Address.City()}, {f.Address.CountryCode()}")
            .RuleFor(x => x.Voltage, f => f.PickRandom(110, 220, 230))
            .RuleFor(x => x.FrequencyHz, f => f.PickRandom(50, 60))
            .RuleFor(x => x.Amperage, f => f.Random.Decimal(1, 16))
            .RuleFor(x => x.ManufacturedDate, f => DateOnly.FromDateTime(f.Date.Past(15, now)))
            .FinishWith((f, machine) =>
            {
                if (f.IndexFaker % 5 == 0)
                    machine.SetStatus(MachineStatus.Maintenance);
                else
                    machine.CompleteEnrollment();
            })
            .Generate(20);

    private static List<Device> CreateDevices(DateTime now) =>
        new Faker<Device>("fr").UseSeed(Seed + 6)
            .RuleFor(x => x.DeviceId, f => f.Random.Guid())
            .RuleFor(x => x.SerialPhysical, f => $"DEVICE-{f.IndexFaker + 1:00000}")
            .RuleFor(x => x.Status, f => f.IndexFaker % 8 == 0 ? DeviceStatus.Revoked : DeviceStatus.Active)
            .RuleFor(x => x.PublicKey, f => DevelopmentPem("PUBLIC KEY", $"device-key-{f.IndexFaker + 1}"))
            .RuleFor(x => x.PublicKeyFingerprint, f => Sha256($"device-key-{f.IndexFaker + 1}"))
            .RuleFor(x => x.KeyType, f => f.PickRandom<DeviceKeyType>().ToString())
            .RuleFor(x => x.KeyAlgorithm, _ => "ecdsa-p256-sha256")
            .RuleFor(x => x.LastCounter, f => f.Random.Long(0, 50_000))
            .RuleFor(x => x.CreatedAtUtc, f => Utc(f.Date.Past(2, now)))
            .RuleFor(x => x.ProvisionedAtUtc, (f, x) => x.CreatedAtUtc.AddMinutes(f.Random.Int(1, 30)))
            .RuleFor(x => x.ActivatedAtUtc, (f, x) => x.ProvisionedAtUtc?.AddMinutes(f.Random.Int(1, 30)))
            .RuleFor(x => x.RevokedAtUtc, (f, x) => x.Status == DeviceStatus.Revoked
                ? Utc(f.Date.Between(x.ActivatedAtUtc ?? x.CreatedAtUtc, now))
                : null)
            .RuleFor(x => x.LastSeenAtUtc, f => Utc(f.Date.Recent(14, now)))
            .RuleFor(x => x.ClientVersion, f => $"1.{f.Random.Int(0, 8)}.{f.Random.Int(0, 20)}")
            .RuleFor(x => x.Notes, f => f.Lorem.Sentence())
            .Generate(20);

    private static List<MachineDevice> CreateMachineDevices(
        IReadOnlyList<Machine> machines,
        IReadOnlyList<Device> devices,
        DateTime now) =>
        Enumerable.Range(0, Math.Min(machines.Count, devices.Count))
            .Select(index => 
            {
                var md = new MachineDevice(
                    machines[index].MachineId,
                    devices[index].DeviceId,
                    now.AddDays(-(index + 1))
                );

                if (devices[index].Status != DeviceStatus.Active)
                {
                    md.Detach(now);
                }

                return md;
            })
            .ToList();

    private static List<CaCertificate> CreateCaCertificates(DateTime now) =>
        new Faker<CaCertificate>("fr").UseSeed(Seed + 7)
            .CustomInstantiator(f =>
            {
                var index = f.IndexFaker + 1;
                var commonName = $"WorldPinball Development CA {index}";
                return new CaCertificate(
                    index == 1 ? CaType.Root : CaType.Intermediate,
                    $"CN={commonName}",
                    index == 1 ? $"CN={commonName}" : "CN=WorldPinball Development CA 1",
                    $"CA-{index:0000}",
                    Sha256($"ca-certificate-{index}"),
                    DevelopmentPem("CERTIFICATE", $"ca-certificate-{index}"),
                    now.AddDays(-30),
                    now.AddYears(index == 1 ? 10 : 5),
                    now);
            })
            .Generate(2);

    private static List<DeviceCertificate> CreateDeviceCertificates(
        IReadOnlyList<Device> devices,
        IReadOnlyList<CaCertificate> caCertificates) =>
        new Faker<DeviceCertificate>("fr").UseSeed(Seed + 8)
            .CustomInstantiator(f =>
            {
                var index = f.IndexFaker;
                var device = devices[index];
                var ca = caCertificates[index % caCertificates.Count];
                var issuedAt = device.ActivatedAtUtc ?? device.CreatedAtUtc;
                return new DeviceCertificate(
                    device.DeviceId,
                    ca.CaId,
                    $"CERT-{index + 1:00000}",
                    Sha256($"device-certificate-{index + 1}"),
                    $"CN={device.SerialPhysical}",
                    ca.SubjectDn,
                    DevelopmentPem("CERTIFICATE", $"device-certificate-{index + 1}"),
                    issuedAt,
                    issuedAt.AddYears(1),
                    issuedAt);
            })
            .Generate(devices.Count);

    private static List<EnrollmentToken> CreateEnrollmentTokens(
        IReadOnlyList<Machine> machines,
        IReadOnlyList<Device> devices,
        DateTime now) =>
        new Faker<EnrollmentToken>("fr").UseSeed(Seed + 9)
            .CustomInstantiator(f =>
            {
                var index = f.IndexFaker;
                return new EnrollmentToken(
                    Sha256($"development-enrollment-token-{index + 1}"),
                    EnrollmentTokenType.DeviceEnroll,
                    DevelopmentUserId,
                    now.AddDays(-(index + 1)),
                    now.AddDays(30),
                    machines[index % machines.Count].MachineId,
                    devices[index % devices.Count].DeviceId,
                    notes: f.Lorem.Sentence());
            })
            .Generate(20);

    private static List<Mission> CreateMissions(
        IReadOnlyList<IpLicense> licenses,
        IReadOnlyList<GameMode> modes,
        DateTime now) =>
        new Faker<Mission>("fr").UseSeed(Seed + 10)
            .RuleFor(x => x.MissionId, f => f.Random.Guid())
            .RuleFor(x => x.IpId, f =>
                licenses[f.Random.Int(0, licenses.Count - 1)].IpId)
            .RuleFor(x => x.ModeId, f =>
                modes[f.Random.Int(0, modes.Count - 1)].ModeId)
            .RuleFor(x => x.Code, f => $"mission-{f.IndexFaker + 1:000}")
            .RuleFor(x => x.Title, f => $"Mission {f.Hacker.Verb()} {f.IndexFaker + 1}")
            .RuleFor(x => x.Description, f => f.Lorem.Paragraph())
            .RuleFor(x => x.MissionType, f => f.PickRandom<MissionType>().ToString())
            .RuleFor(x => x.IsActive, f => f.IndexFaker % 10 != 0)
            .RuleFor(x => x.CreatedAtUtc, _ => now)
            .Generate(30);

    private static List<GameSession> CreateGameSessions(
        IReadOnlyList<MachineDevice> links,
        IReadOnlyList<GameMode> modes,
        DateTime now) =>
        new Faker<GameSession>("fr").UseSeed(Seed + 11)
            .CustomInstantiator(f =>
            {
                var link = links[f.IndexFaker % links.Count];
                var mode = modes[f.Random.Int(0, modes.Count - 1)];
                var startedAt = Utc(f.Date.Recent(90, now));
                var nonce = $"development-nonce-{f.IndexFaker + 1:00000}";
                var clientVersion = $"1.{f.Random.Int(0, 8)}.{f.Random.Int(0, 20)}";

                var session = new GameSession(
                    link.MachineId,
                    link.DeviceId,
                    mode.ModeId,
                    clientVersion,
                    nonce,
                    startedAt);

                session.AddPlayer(1, f.Random.Guid(), "DEV", requiresRegisteredUser: false);
                session.Launch(startedAt);
                session.End(startedAt.AddMinutes(f.Random.Int(3, 45)));

                return session;
            })
            .Generate(60);

    private static List<ScoreEntry> CreateScoreEntries(
        IReadOnlyList<GameSession> sessions,
        IReadOnlyList<Machine> machines,
        DateTime now)
    {
        var machinesById = machines.ToDictionary(x => x.MachineId);
        return new Faker<ScoreEntry>("fr").UseSeed(Seed + 12)
            .CustomInstantiator(f =>
            {
                var session = sessions[f.IndexFaker];
                var assignedUserId = (f.IndexFaker % 4 == 0)
                    ? "a2c66552-2b07-42cb-9814-0f5abacfd902"
                    : $"development-user-{(f.IndexFaker % 12) + 1:00}";

                var score = new ScoreEntry(
                    session.SessionId,
                    1,
                    session.DeviceId,
                    session.MachineId,
                    machinesById[session.MachineId].IpId,
                    session.ModeId,
                    f.Random.Int(10_000, 25_000_000),
                    session.EndedAtUtc ?? now,
                    assignedUserId);

                if (f.IndexFaker % 12 == 0)
                {
                    score.Invalidate("Score marked for development review.");
                }

                return score;
            })
            .Generate(sessions.Count);
    }

    private static List<MissionProgress> CreateMissionProgress(
        IReadOnlyList<Mission> missions,
        DateTime now) =>
        new Faker<MissionProgress>("fr").UseSeed(Seed + 13)
            .RuleFor(x => x.MissionProgressId, f => f.Random.Guid())
            .RuleFor(x => x.MissionId, f => missions[f.IndexFaker % missions.Count].MissionId)
            .RuleFor(x => x.UserId, f => $"development-user-{(f.IndexFaker / missions.Count) + 1:00}")
            .RuleFor(x => x.CurrentValue, f => f.Random.Int(0, 100))
            .RuleFor(x => x.Status, f => f.PickRandom(
                MissionProgressStatus.NotStarted,
                MissionProgressStatus.InProgress,
                MissionProgressStatus.Completed))
            .RuleFor(x => x.CompletedAtUtc, (f, x) => x.Status == MissionProgressStatus.Completed
                ? Utc(f.Date.Recent(30, now))
                : null)
            .RuleFor(x => x.UpdatedAtUtc, f => Utc(f.Date.Recent(14, now)))
            .Generate(missions.Count * 3);

    private static List<CertificateRevocation> CreateCertificateRevocations(
        IReadOnlyList<DeviceCertificate> certificates,
        DateTime now) =>
        new Faker<CertificateRevocation>("fr").UseSeed(Seed + 14)
            .CustomInstantiator(f =>
            {
                var revoked = certificates.Where((_, index) => index % 8 == 0).ToList();
                var certificate = revoked[f.IndexFaker];
                var revokedAt = now.AddDays(-(f.IndexFaker + 1));
                const string notes = "Development seed revocation";
                certificate.MarkRevoked(revokedAt, notes);
                return new CertificateRevocation(
                    certificate.DeviceCertId,
                    revokedAt,
                    f.PickRandom<RevocationReason>(),
                    DevelopmentUserId,
                    notes);
            })
            .Generate(certificates.Count(x => true) / 8);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string DevelopmentPem(string label, string value) =>
        $"-----BEGIN {label}-----\n{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}\n-----END {label}-----";

    private static DateTime Utc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);
    
    
    private static List<RolePermission> CreateRolePermissions() =>
    [
        new() { Role = "Admin", Permission = "MachinesProvision" },
        new() { Role = "Admin", Permission = "MachinesReissueToken" },
        new() { Role = "Admin", Permission = "ShipmentsDispatch" },
        new() { Role = "Admin", Permission = "ShipmentsCancel" },
        new() { Role = "Admin", Permission = "SessionsPlay" },
        new() { Role = "Technician", Permission = "MachinesProvision" },
        new() { Role = "Technician", Permission = "MachinesReissueToken" },
        new() { Role = "Operator", Permission = "ShipmentsDispatch" },
        new() { Role = "Player", Permission = "SessionsPlay" }
    ];
}
