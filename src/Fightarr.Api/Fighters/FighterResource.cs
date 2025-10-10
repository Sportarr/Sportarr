using System;
using NzbDrone.Core.Fights;
using Fightarr.Http.REST;

namespace Fightarr.Api.Fighters
{
    public class FighterResource : RestResource
    {
        public int FightarrFighterId { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public string Nickname { get; set; }
        public string WeightClass { get; set; }
        public string Nationality { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }
        public int NoContests { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Height { get; set; }
        public string Reach { get; set; }
        public string ImageUrl { get; set; }
        public string Bio { get; set; }
        public bool IsActive { get; set; }
        public string Record => $"{Wins}-{Losses}-{Draws}";
    }

    public static class FighterResourceMapper
    {
        public static FighterResource ToResource(this Fighter model)
        {
            if (model == null)
                return null;

            return new FighterResource
            {
                Id = model.Id,
                FightarrFighterId = model.FightarrFighterId,
                Name = model.Name,
                Slug = model.Slug,
                Nickname = model.Nickname,
                WeightClass = model.WeightClass,
                Nationality = model.Nationality,
                Wins = model.Wins,
                Losses = model.Losses,
                Draws = model.Draws,
                NoContests = model.NoContests,
                BirthDate = model.BirthDate,
                Height = model.Height,
                Reach = model.Reach,
                ImageUrl = model.ImageUrl,
                Bio = model.Bio,
                IsActive = model.IsActive
            };
        }
    }
}
