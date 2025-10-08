using System.Collections.Generic;
using NzbDrone.Core.Tv;

namespace Fightarr.Api.V3.SeasonPass
{
    public class SeasonPassResource
    {
        public List<SeasonPassSeriesResource> Series { get; set; }
        public MonitoringOptions MonitoringOptions { get; set; }
    }
}
