using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Parser.Model;
using Fightarr.Http;

namespace Fightarr.Api.V3.Indexers
{
    [FightarrApiController]
    public class IndexerFlagController : Controller
    {
        [HttpGet]
        public List<IndexerFlagResource> GetAll()
        {
            return Enum.GetValues(typeof(IndexerFlags)).Cast<IndexerFlags>().Select(f => new IndexerFlagResource
            {
                Id = (int)f,
                Name = f.ToString()
            }).ToList();
        }
    }
}
