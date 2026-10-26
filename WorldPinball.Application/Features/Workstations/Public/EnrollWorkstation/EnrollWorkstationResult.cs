namespace WorldPinball.Application.Features.Workstations.Public.EnrollWorkstation;

public abstract record EnrollWorkstationResult
{
    public sealed record Success(
        string CertPem,
        string SerialNumber,
        string ThumbprintSha256,
        DateTime NotAfterUtc
    ) : EnrollWorkstationResult;

    public sealed record InvalidToken(string Reason) : EnrollWorkstationResult;
    public sealed record InvalidPublicKey(string Reason) : EnrollWorkstationResult;
}