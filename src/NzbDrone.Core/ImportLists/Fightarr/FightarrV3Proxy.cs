using System;
using System.Collections.Generic;
using System.Net;
using FluentValidation.Results;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.ImportLists.Fightarr
{
    public interface IFightarrV3Proxy
    {
        List<FightarrSeries> GetSeries(FightarrSettings settings);
        List<FightarrProfile> GetQualityProfiles(FightarrSettings settings);
        List<FightarrProfile> GetLanguageProfiles(FightarrSettings settings);
        List<FightarrRootFolder> GetRootFolders(FightarrSettings settings);
        List<FightarrTag> GetTags(FightarrSettings settings);
        ValidationFailure Test(FightarrSettings settings);
    }

    public class FightarrV3Proxy : IFightarrV3Proxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly ILocalizationService _localizationService;

        public FightarrV3Proxy(IHttpClient httpClient, ILocalizationService localizationService, Logger logger)
        {
            _httpClient = httpClient;
            _localizationService = localizationService;
            _logger = logger;
        }

        public List<FightarrSeries> GetSeries(FightarrSettings settings)
        {
            return Execute<FightarrSeries>("/api/v3/series", settings);
        }

        public List<FightarrProfile> GetQualityProfiles(FightarrSettings settings)
        {
            return Execute<FightarrProfile>("/api/v3/qualityprofile", settings);
        }

        public List<FightarrProfile> GetLanguageProfiles(FightarrSettings settings)
        {
            return Execute<FightarrProfile>("/api/v3/languageprofile", settings);
        }

        public List<FightarrRootFolder> GetRootFolders(FightarrSettings settings)
        {
            return Execute<FightarrRootFolder>("api/v3/rootfolder", settings);
        }

        public List<FightarrTag> GetTags(FightarrSettings settings)
        {
            return Execute<FightarrTag>("/api/v3/tag", settings);
        }

        public ValidationFailure Test(FightarrSettings settings)
        {
            try
            {
                GetSeries(settings);
            }
            catch (HttpException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.Error(ex, "API Key is invalid");
                    return new ValidationFailure("ApiKey", _localizationService.GetLocalizedString("ImportListsValidationInvalidApiKey"));
                }

                if (ex.Response.HasHttpRedirect)
                {
                    _logger.Error(ex, "Fightarr returned redirect and is invalid");
                    return new ValidationFailure("BaseUrl", _localizationService.GetLocalizedString("ImportListsFightarrValidationInvalidUrl"));
                }

                _logger.Error(ex, "Unable to connect to import list.");
                return new ValidationFailure(string.Empty, _localizationService.GetLocalizedString("ImportListsValidationUnableToConnectException", new Dictionary<string, object> { { "exceptionMessage", ex.Message } }));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to connect to import list.");
                return new ValidationFailure(string.Empty, _localizationService.GetLocalizedString("ImportListsValidationUnableToConnectException", new Dictionary<string, object> { { "exceptionMessage", ex.Message } }));
            }

            return null;
        }

        private List<TResource> Execute<TResource>(string resource, FightarrSettings settings)
        {
            if (settings.BaseUrl.IsNullOrWhiteSpace() || settings.ApiKey.IsNullOrWhiteSpace())
            {
                return new List<TResource>();
            }

            var baseUrl = settings.BaseUrl.TrimEnd('/');

            var request = new HttpRequestBuilder(baseUrl).Resource(resource)
                .Accept(HttpAccept.Json)
                .SetHeader("X-Api-Key", settings.ApiKey)
                .Build();

            var response = _httpClient.Get(request);

            if ((int)response.StatusCode >= 300)
            {
                throw new HttpException(response);
            }

            var results = JsonConvert.DeserializeObject<List<TResource>>(response.Content);

            return results;
        }
    }
}
