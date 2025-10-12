using System;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Fightarr.Http
{
    public class VersionedApiControllerAttribute : Attribute, IRouteTemplateProvider, IEnableCorsAttribute, IApiBehaviorMetadata
    {
        public const string API_CORS_POLICY = "ApiCorsPolicy";
        public const string CONTROLLER_RESOURCE = "[controller]";

        public VersionedApiControllerAttribute(int version, string resource = CONTROLLER_RESOURCE)
        {
            Resource = resource;
            // Version 0 means no version prefix (just /api/), otherwise /api/v{version}/
            Template = version == 0 ? $"api/{resource}" : $"api/v{version}/{resource}";
            PolicyName = API_CORS_POLICY;
            Version = version;
        }

        public string Resource { get; }
        public string Template { get; }
        public int? Order => 2;
        public string Name { get; set; }
        public string PolicyName { get; set; }
        public int Version { get; set; }
    }

    /// <summary>
    /// Main Fightarr API controller attribute for unversioned /api endpoints
    /// </summary>
    public class FightarrApiControllerAttribute : VersionedApiControllerAttribute
    {
        public FightarrApiControllerAttribute(string resource = "[controller]")
            : base(0, resource) // Version 0 = no version prefix, maps to /api/
        {
        }
    }

    public class V5ApiControllerAttribute : VersionedApiControllerAttribute
    {
        public V5ApiControllerAttribute(string resource = "[controller]")
            : base(5, resource)
        {
        }
    }
}
