namespace Sportarr.Api.Services;

/// <summary>
/// Common team codes for the four major US leagues and the Indian Premier League.
/// Release groups often name a game by these codes only ("NBA 2026 10 20 OKC SAS",
/// "IPL 2024 MI vs CSK"). The metadata hub sends a code for every team. The app
/// keeps a team row only for teams the user follows, so the opponent of a followed
/// team has no code on this side. This table fills that gap until the app stores
/// the code of every league team. A two-letter code is listed only when no other
/// team of the same league uses it. Each caller sets its own proof before it takes
/// a code from a title.
/// </summary>
public static class TeamShortCodeData
{
    private static readonly Dictionary<string, string[]> Nba = new(StringComparer.OrdinalIgnoreCase)
    {
        // Variants cover the codes that other schedules use for the same team.
        { "Atlanta Hawks", new[] { "ATL" } },
        { "Boston Celtics", new[] { "BOS" } },
        { "Brooklyn Nets", new[] { "BKN", "BRK" } },
        { "Charlotte Hornets", new[] { "CHA", "CHO" } },
        { "Chicago Bulls", new[] { "CHI" } },
        { "Cleveland Cavaliers", new[] { "CLE" } },
        { "Dallas Mavericks", new[] { "DAL" } },
        { "Denver Nuggets", new[] { "DEN" } },
        { "Detroit Pistons", new[] { "DET" } },
        { "Golden State Warriors", new[] { "GSW" } },
        { "Houston Rockets", new[] { "HOU" } },
        { "Indiana Pacers", new[] { "IND" } },
        { "Los Angeles Clippers", new[] { "LAC" } },
        { "Los Angeles Lakers", new[] { "LAL" } },
        { "Memphis Grizzlies", new[] { "MEM" } },
        { "Miami Heat", new[] { "MIA" } },
        { "Milwaukee Bucks", new[] { "MIL" } },
        { "Minnesota Timberwolves", new[] { "MIN" } },
        { "New Orleans Pelicans", new[] { "NOP" } },
        { "New York Knicks", new[] { "NYK" } },
        { "Oklahoma City Thunder", new[] { "OKC" } },
        { "Orlando Magic", new[] { "ORL" } },
        { "Philadelphia 76ers", new[] { "PHI" } },
        { "Phoenix Suns", new[] { "PHX", "PHO" } },
        { "Portland Trail Blazers", new[] { "POR" } },
        { "Sacramento Kings", new[] { "SAC" } },
        { "San Antonio Spurs", new[] { "SAS" } },
        { "Toronto Raptors", new[] { "TOR" } },
        { "Utah Jazz", new[] { "UTA", "UTAH" } },
        { "Washington Wizards", new[] { "WAS", "WSH" } },
    };

    private static readonly Dictionary<string, string[]> Nfl = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Arizona Cardinals", new[] { "ARI" } },
        { "Atlanta Falcons", new[] { "ATL" } },
        { "Baltimore Ravens", new[] { "BAL" } },
        { "Buffalo Bills", new[] { "BUF" } },
        { "Carolina Panthers", new[] { "CAR" } },
        { "Chicago Bears", new[] { "CHI" } },
        { "Cincinnati Bengals", new[] { "CIN" } },
        { "Cleveland Browns", new[] { "CLE" } },
        { "Dallas Cowboys", new[] { "DAL" } },
        { "Denver Broncos", new[] { "DEN" } },
        { "Detroit Lions", new[] { "DET" } },
        { "Houston Texans", new[] { "HOU" } },
        { "Indianapolis Colts", new[] { "IND" } },
        { "Jacksonville Jaguars", new[] { "JAX", "JAC" } },
        { "Los Angeles Chargers", new[] { "LAC" } },
        { "Los Angeles Rams", new[] { "LAR", "LA" } },
        { "Miami Dolphins", new[] { "MIA" } },
        { "Minnesota Vikings", new[] { "MIN" } },
        { "New York Giants", new[] { "NYG" } },
        { "New York Jets", new[] { "NYJ" } },
        { "Philadelphia Eagles", new[] { "PHI" } },
        { "Pittsburgh Steelers", new[] { "PIT" } },
        { "Seattle Seahawks", new[] { "SEA" } },
        { "Tennessee Titans", new[] { "TEN" } },
        { "Washington Commanders", new[] { "WAS", "WSH" } },
        { "Green Bay Packers", new[] { "GB" } },
        { "Kansas City Chiefs", new[] { "KC" } },
        { "Las Vegas Raiders", new[] { "LV" } },
        { "New England Patriots", new[] { "NE" } },
        { "New Orleans Saints", new[] { "NO" } },
        { "San Francisco 49ers", new[] { "SF" } },
        { "Tampa Bay Buccaneers", new[] { "TB" } },
    };

    private static readonly Dictionary<string, string[]> Nhl = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Anaheim Ducks", new[] { "ANA" } },
        { "Arizona Coyotes", new[] { "ARI" } },
        { "Boston Bruins", new[] { "BOS" } },
        { "Buffalo Sabres", new[] { "BUF" } },
        { "Calgary Flames", new[] { "CGY" } },
        { "Carolina Hurricanes", new[] { "CAR" } },
        { "Chicago Blackhawks", new[] { "CHI" } },
        { "Colorado Avalanche", new[] { "COL" } },
        { "Columbus Blue Jackets", new[] { "CBJ" } },
        { "Dallas Stars", new[] { "DAL" } },
        { "Detroit Red Wings", new[] { "DET" } },
        { "Edmonton Oilers", new[] { "EDM" } },
        { "Florida Panthers", new[] { "FLA" } },
        { "Los Angeles Kings", new[] { "LAK", "LA" } },
        { "Minnesota Wild", new[] { "MIN" } },
        { "Montreal Canadiens", new[] { "MTL" } },
        { "Nashville Predators", new[] { "NSH" } },
        { "New Jersey Devils", new[] { "NJD", "NJ" } },
        { "New York Islanders", new[] { "NYI" } },
        { "New York Rangers", new[] { "NYR" } },
        { "Ottawa Senators", new[] { "OTT" } },
        { "Philadelphia Flyers", new[] { "PHI" } },
        { "Pittsburgh Penguins", new[] { "PIT" } },
        { "San Jose Sharks", new[] { "SJS", "SJ" } },
        { "Seattle Kraken", new[] { "SEA" } },
        { "St. Louis Blues", new[] { "STL" } },
        { "Tampa Bay Lightning", new[] { "TBL", "TB" } },
        { "Toronto Maple Leafs", new[] { "TOR" } },
        { "Utah Hockey Club", new[] { "UTA", "UHC" } },
        { "Utah Mammoth", new[] { "UTA", "UHC" } },
        { "Vancouver Canucks", new[] { "VAN" } },
        { "Vegas Golden Knights", new[] { "VGK" } },
        { "Washington Capitals", new[] { "WSH" } },
        { "Winnipeg Jets", new[] { "WPG" } },
    };

    private static readonly Dictionary<string, string[]> Mlb = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Arizona Diamondbacks", new[] { "ARI" } },
        { "Athletics", new[] { "ATH", "OAK" } },
        { "Oakland Athletics", new[] { "OAK", "ATH" } },
        { "Atlanta Braves", new[] { "ATL" } },
        { "Baltimore Orioles", new[] { "BAL" } },
        { "Boston Red Sox", new[] { "BOS" } },
        { "Chicago Cubs", new[] { "CHC" } },
        { "Chicago White Sox", new[] { "CHW", "CWS" } },
        { "Cincinnati Reds", new[] { "CIN" } },
        { "Cleveland Guardians", new[] { "CLE" } },
        { "Colorado Rockies", new[] { "COL" } },
        { "Detroit Tigers", new[] { "DET" } },
        { "Houston Astros", new[] { "HOU" } },
        { "Kansas City Royals", new[] { "KCR", "KC" } },
        { "Los Angeles Angels", new[] { "LAA" } },
        { "Los Angeles Dodgers", new[] { "LAD" } },
        { "Miami Marlins", new[] { "MIA" } },
        { "Milwaukee Brewers", new[] { "MIL" } },
        { "Minnesota Twins", new[] { "MIN" } },
        { "New York Mets", new[] { "NYM" } },
        { "New York Yankees", new[] { "NYY" } },
        { "Philadelphia Phillies", new[] { "PHI" } },
        { "Pittsburgh Pirates", new[] { "PIT" } },
        { "San Diego Padres", new[] { "SDP", "SD" } },
        { "San Francisco Giants", new[] { "SFG", "SF" } },
        { "Seattle Mariners", new[] { "SEA" } },
        { "St. Louis Cardinals", new[] { "STL" } },
        { "Tampa Bay Rays", new[] { "TBR", "TB" } },
        { "Texas Rangers", new[] { "TEX" } },
        { "Toronto Blue Jays", new[] { "TOR" } },
        { "Washington Nationals", new[] { "WSH", "WSN" } },
    };

    // A renamed franchise lists the codes of all its names, so each name
    // finds the same team.
    private static readonly Dictionary<string, string[]> Ipl = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Chennai Super Kings", new[] { "CSK" } },
        { "Delhi Capitals", new[] { "DC", "DD" } },
        { "Delhi Daredevils", new[] { "DC", "DD" } },
        { "Gujarat Titans", new[] { "GT" } },
        { "Kolkata Knight Riders", new[] { "KKR" } },
        { "Lucknow Super Giants", new[] { "LSG" } },
        { "Mumbai Indians", new[] { "MI" } },
        { "Punjab Kings", new[] { "PBKS", "KXIP" } },
        { "Kings XI Punjab", new[] { "PBKS", "KXIP" } },
        { "Rajasthan Royals", new[] { "RR" } },
        { "Royal Challengers Bangalore", new[] { "RCB" } },
        { "Royal Challengers Bengaluru", new[] { "RCB" } },
        { "Sunrisers Hyderabad", new[] { "SRH" } },
    };

    private static readonly Dictionary<string, Dictionary<string, string[]>> Leagues = new(StringComparer.OrdinalIgnoreCase)
    {
        { "NBA", Nba },
        { "NFL", Nfl },
        { "NHL", Nhl },
        { "MLB", Mlb },
        { "IPL", Ipl },
    };

    private static readonly Dictionary<string, string[]> Codes = BuildFlatTable();

    // A name in two leagues keeps the codes of the first league in the list
    // above. A test keeps the tables free of such names, so the rule only
    // stops a startup crash.
    private static Dictionary<string, string[]> BuildFlatTable()
    {
        var codes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var league in Leagues.Values)
        {
            foreach (var (team, teamCodes) in league)
                codes.TryAdd(team, teamCodes);
        }
        return codes;
    }

    /// <summary>
    /// Team names that more than one league table lists.
    /// </summary>
    internal static IReadOnlyList<string> NamesInMoreThanOneLeague() =>
        Leagues.Values.SelectMany(league => league.Keys)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

    /// <summary>
    /// Returns the teams and codes of one league, or an empty table when the league has none.
    /// The key is the league key of the match scorer, such as "NBA" or "IPL".
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> LeagueTeams(string? leagueKey)
    {
        if (string.IsNullOrWhiteSpace(leagueKey)) return EmptyLeague;
        return Leagues.TryGetValue(leagueKey, out var teams) ? teams : EmptyLeague;
    }

    private static readonly Dictionary<string, string[]> EmptyLeague = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the codes known for a team, or an empty list when the team is not in the table.
    /// The match is exact on the canonical team name, so a similar name never borrows codes.
    /// </summary>
    public static IReadOnlyList<string> CodesFor(string? teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName)) return Array.Empty<string>();
        return Codes.TryGetValue(teamName.Trim(), out var codes) ? codes : Array.Empty<string>();
    }
}
