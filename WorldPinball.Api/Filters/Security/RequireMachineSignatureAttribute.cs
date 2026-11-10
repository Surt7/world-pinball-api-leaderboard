using Microsoft.AspNetCore.Mvc;

namespace WorldPinball.Api.Filters.Security;

/// <summary>
/// Enforces ECDSA machine authentication on controller actions via <see cref="MachineSignatureFilter"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class RequireMachineSignatureAttribute : TypeFilterAttribute
{
    public RequireMachineSignatureAttribute() : base(typeof(MachineSignatureFilter))
    {
    }
}