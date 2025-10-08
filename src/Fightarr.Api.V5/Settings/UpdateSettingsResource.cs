using NzbDrone.Core.Update;
using Fightarr.Http.REST;

namespace Fightarr.Api.V5.Settings;

public class UpdateSettingsResource : RestResource
{
    public string? Branch { get; set; }
    public bool UpdateAutomatically { get; set; }
    public UpdateMechanism UpdateMechanism { get; set; }
    public string? UpdateScriptPath { get; set; }
}
