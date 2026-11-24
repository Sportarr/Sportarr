using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Gives a fixed update method, as if the computer were checked. The
/// default is a native install where the built-in update works.
/// </summary>
public sealed class FakeUpdateMethodSource(UpdateMethod? method = null) : IUpdateMethodSource
{
    public UpdateMethod Describe(Config config) => method ?? new UpdateMethod(UpdateMechanisms.BuiltIn, true, null);
}
