using System;
using System.Collections.Generic;
using NzbDrone.Core.Fights;
using Fightarr.Http.REST;

namespace Fightarr.Api.Events
{
    public class EventResource : RestResource
    {
        public int FightarrEventId { get; set; }
        public int OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public string OrganizationType { get; set; }
        public string Title { get; set; }
        public string CleanTitle { get; set; }
        public string Slug { get; set; }
        public string EventNumber { get; set; }
        public DateTime EventDate { get; set; }
        public string EventType { get; set; }
        public string Location { get; set; }
        public string Venue { get; set; }
        public string Broadcaster { get; set; }
        public string Overview { get; set; }
        public string Status { get; set; }
        public bool Monitored { get; set; }
        public string PosterUrl { get; set; }
        public string BannerUrl { get; set; }
        public string OrganizationLogoUrl { get; set; }
        public List<FightCardResource> FightCards { get; set; }
    }

    public class FightCardResource
    {
        public int Id { get; set; }
        public int FightEventId { get; set; }
        public int CardNumber { get; set; }
        public string CardSection { get; set; }
        public string Title { get; set; }
        public DateTime AirDateUtc { get; set; }
        public bool Monitored { get; set; }
        public List<FightResource> Fights { get; set; }
    }

    public class FightResource
    {
        public int Id { get; set; }
        public int FightarrFightId { get; set; }
        public int Fighter1Id { get; set; }
        public string Fighter1Name { get; set; }
        public string Fighter1Record { get; set; }
        public int Fighter2Id { get; set; }
        public string Fighter2Name { get; set; }
        public string Fighter2Record { get; set; }
        public string WeightClass { get; set; }
        public bool IsTitleFight { get; set; }
        public bool IsMainEvent { get; set; }
        public int FightOrder { get; set; }
        public string Result { get; set; }
        public string Method { get; set; }
        public int? Round { get; set; }
        public string Time { get; set; }
        public string Referee { get; set; }
        public string Notes { get; set; }
    }

    public static class EventResourceMapper
    {
        public static EventResource ToResource(this FightEvent model)
        {
            if (model == null)
                return null;

            return new EventResource
            {
                Id = model.Id,
                FightarrEventId = model.FightarrEventId,
                OrganizationId = model.OrganizationId,
                OrganizationName = model.OrganizationName,
                OrganizationType = model.OrganizationType,
                Title = model.Title,
                CleanTitle = model.CleanTitle,
                Slug = model.Slug,
                EventNumber = model.EventNumber,
                EventDate = model.EventDate,
                EventType = model.EventType,
                Location = model.Location,
                Venue = model.Venue,
                Broadcaster = model.Broadcaster,
                Overview = model.Overview,
                Status = model.Status,
                Monitored = model.Monitored,
                PosterUrl = model.PosterUrl,
                BannerUrl = model.BannerUrl,
                OrganizationLogoUrl = model.OrganizationLogoUrl,
                FightCards = model.FightCards?.Select(ToResource).ToList()
            };
        }

        public static FightCardResource ToResource(this FightCard model)
        {
            if (model == null)
                return null;

            return new FightCardResource
            {
                Id = model.Id,
                FightEventId = model.FightEventId,
                CardNumber = model.CardNumber,
                CardSection = model.CardSection,
                Title = model.Title,
                AirDateUtc = model.AirDateUtc,
                Monitored = model.Monitored,
                Fights = model.Fights?.Select(ToResource).ToList()
            };
        }

        public static FightResource ToResource(this Fight model)
        {
            if (model == null)
                return null;

            return new FightResource
            {
                Id = model.Id,
                FightarrFightId = model.FightarrFightId,
                Fighter1Id = model.Fighter1Id,
                Fighter1Name = model.Fighter1Name,
                Fighter1Record = model.Fighter1Record,
                Fighter2Id = model.Fighter2Id,
                Fighter2Name = model.Fighter2Name,
                Fighter2Record = model.Fighter2Record,
                WeightClass = model.WeightClass,
                IsTitleFight = model.IsTitleFight,
                IsMainEvent = model.IsMainEvent,
                FightOrder = model.FightOrder,
                Result = model.Result,
                Method = model.Method,
                Round = model.Round,
                Time = model.Time,
                Referee = model.Referee,
                Notes = model.Notes
            };
        }

        public static List<EventResource> ToResource(this IEnumerable<FightEvent> models)
        {
            return models?.Select(ToResource).ToList();
        }
    }
}
