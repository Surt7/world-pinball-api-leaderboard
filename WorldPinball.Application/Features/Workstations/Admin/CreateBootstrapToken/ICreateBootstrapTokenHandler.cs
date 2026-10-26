using WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken.commands;

namespace WorldPinball.Application.Features.Workstations.Admin.CreateBootstrapToken;

public interface ICreateBootstrapTokenHandler
{
    Task<CreateBootstrapTokenResult> HandleAsync(
        CreateBootstrapTokenCommand command,
        CancellationToken ct = default);
}