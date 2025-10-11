using NzbDrone.Common.Http;

namespace NzbDrone.Common.Cloud
{
    public interface IFightarrCloudRequestBuilder
    {
        IHttpRequestBuilderFactory Services { get; }
        IHttpRequestBuilderFactory SkyHookTvdb { get; }
    }

    public class FightarrCloudRequestBuilder : IFightarrCloudRequestBuilder
    {
        public FightarrCloudRequestBuilder()
        {
            Services = new HttpRequestBuilder("https://services.fightarr.net/v1/")
                .CreateFactory();

            SkyHookTvdb = new HttpRequestBuilder("https://skyhook.fightarr.net/v1/tvdb/{route}/{language}/")
                .SetSegment("language", "en")
                .CreateFactory();
        }

        public IHttpRequestBuilderFactory Services { get; }

        public IHttpRequestBuilderFactory SkyHookTvdb { get; }
    }
}
