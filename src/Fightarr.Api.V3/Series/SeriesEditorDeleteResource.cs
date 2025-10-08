using System.Collections.Generic;

namespace Fightarr.Api.V3.Series
{
    public class SeriesEditorDeleteResource
    {
        public List<int> SeriesIds { get; set; }
        public bool DeleteFiles { get; set; }
    }
}
