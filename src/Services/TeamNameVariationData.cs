namespace Sportarr.Api.Services;

/// <summary>
/// Shared team name variation data used by both ReleaseMatchingService and ReleaseMatchScorer.
/// Maps canonical team names (matching TheSportsDB naming) to their common abbreviations,
/// alternate names, and nicknames found in release titles.
/// </summary>
public static class TeamNameVariationData
{
    /// <summary>
    /// Team name variations dictionary.
    /// Key: canonical team name (case-insensitive), Value: alternate names to check in releases.
    /// Includes abbreviations, short forms, city abbreviations, and common nicknames.
    /// </summary>
    public static readonly Dictionary<string, string[]> Variations = new(StringComparer.OrdinalIgnoreCase)
    {
        // ============================================================
        // NBA Teams (30)
        // ============================================================
        { "Atlanta Hawks", new[] { "ATL", "Hawks" } },
        { "Boston Celtics", new[] { "BOS", "Celtics" } },
        { "Brooklyn Nets", new[] { "BKN", "Nets" } },
        { "Charlotte Hornets", new[] { "CHA", "Hornets" } },
        { "Chicago Bulls", new[] { "CHI", "Bulls" } },
        { "Cleveland Cavaliers", new[] { "CLE", "Cavaliers", "Cavs" } },
        { "Dallas Mavericks", new[] { "DAL", "Mavericks", "Mavs" } },
        { "Denver Nuggets", new[] { "DEN", "Nuggets" } },
        { "Detroit Pistons", new[] { "DET", "Pistons" } },
        { "Golden State Warriors", new[] { "GSW", "GS Warriors", "Warriors" } },
        { "Houston Rockets", new[] { "HOU", "Rockets" } },
        { "Indiana Pacers", new[] { "IND", "Pacers" } },
        { "Los Angeles Clippers", new[] { "LAC", "LA Clippers", "Clippers" } },
        { "Los Angeles Lakers", new[] { "LAL", "LA Lakers", "Lakers" } },
        { "Memphis Grizzlies", new[] { "MEM", "Grizzlies" } },
        { "Miami Heat", new[] { "MIA", "Heat" } },
        { "Milwaukee Bucks", new[] { "MIL", "Bucks" } },
        { "Minnesota Timberwolves", new[] { "MIN", "Timberwolves", "T-Wolves", "Wolves" } },
        { "New Orleans Pelicans", new[] { "NOP", "NO Pelicans", "NOLA Pelicans", "Pelicans" } },
        { "New York Knicks", new[] { "NYK", "NY Knicks", "Knicks" } },
        { "Oklahoma City Thunder", new[] { "OKC", "OKC Thunder", "Thunder" } },
        { "Orlando Magic", new[] { "ORL", "Magic" } },
        { "Philadelphia 76ers", new[] { "PHI", "Philly 76ers", "76ers", "Sixers" } },
        { "Phoenix Suns", new[] { "PHX", "Suns" } },
        { "Portland Trail Blazers", new[] { "POR", "Portland Trailblazers", "Trailblazers", "Trail Blazers", "Blazers" } },
        { "Sacramento Kings", new[] { "SAC", "Kings" } },
        { "San Antonio Spurs", new[] { "SAS", "SA Spurs", "Spurs" } },
        { "Toronto Raptors", new[] { "TOR", "Raptors" } },
        { "Utah Jazz", new[] { "UTA", "Jazz" } },
        { "Washington Wizards", new[] { "WAS", "Wizards" } },

        // ============================================================
        // NFL Teams (32)
        // ============================================================
        { "Arizona Cardinals", new[] { "ARI", "Cardinals" } },
        { "Atlanta Falcons", new[] { "ATL Falcons", "Falcons" } },
        { "Baltimore Ravens", new[] { "BAL", "Ravens" } },
        { "Buffalo Bills", new[] { "BUF", "Bills" } },
        { "Carolina Panthers", new[] { "CAR", "Panthers" } },
        { "Chicago Bears", new[] { "CHI Bears", "Bears" } },
        { "Cincinnati Bengals", new[] { "CIN", "Bengals" } },
        { "Cleveland Browns", new[] { "CLE Browns", "Browns" } },
        { "Dallas Cowboys", new[] { "DAL Cowboys", "Cowboys" } },
        { "Denver Broncos", new[] { "DEN Broncos", "Broncos" } },
        { "Detroit Lions", new[] { "DET Lions", "Lions" } },
        { "Green Bay Packers", new[] { "GB", "GB Packers", "Packers" } },
        { "Houston Texans", new[] { "HOU Texans", "Texans" } },
        { "Indianapolis Colts", new[] { "IND Colts", "Colts" } },
        { "Jacksonville Jaguars", new[] { "JAX", "Jaguars", "Jags" } },
        { "Kansas City Chiefs", new[] { "KC", "KC Chiefs", "Chiefs" } },
        { "Las Vegas Raiders", new[] { "LV", "LV Raiders", "Raiders" } },
        { "Los Angeles Chargers", new[] { "LAC Chargers", "LA Chargers", "Chargers" } },
        { "Los Angeles Rams", new[] { "LAR", "LA Rams", "Rams" } },
        { "Miami Dolphins", new[] { "MIA Dolphins", "Dolphins" } },
        { "Minnesota Vikings", new[] { "MIN Vikings", "Vikings" } },
        { "New England Patriots", new[] { "NE", "NE Patriots", "Patriots", "Pats" } },
        { "New Orleans Saints", new[] { "NO", "NO Saints", "Saints" } },
        { "New York Giants", new[] { "NYG", "NY Giants", "Giants" } },
        { "New York Jets", new[] { "NYJ", "NY Jets", "Jets" } },
        { "Philadelphia Eagles", new[] { "PHI Eagles", "Eagles" } },
        { "Pittsburgh Steelers", new[] { "PIT", "Steelers" } },
        { "San Francisco 49ers", new[] { "SF", "SF 49ers", "49ers", "Niners" } },
        { "Seattle Seahawks", new[] { "SEA", "Seahawks" } },
        { "Tampa Bay Buccaneers", new[] { "TB", "Buccaneers", "Bucs" } },
        { "Tennessee Titans", new[] { "TEN", "Titans" } },
        { "Washington Commanders", new[] { "WAS Commanders", "Commanders" } },

        // ============================================================
        // MLB Teams (30)
        // ============================================================
        { "Arizona Diamondbacks", new[] { "ARI Diamondbacks", "Diamondbacks", "D-Backs", "DBacks" } },
        { "Atlanta Braves", new[] { "ATL Braves", "Braves" } },
        { "Baltimore Orioles", new[] { "BAL Orioles", "Orioles" } },
        { "Boston Red Sox", new[] { "BOS Red Sox", "Red Sox", "RedSox", "BoSox" } },
        { "Chicago Cubs", new[] { "CHC", "Cubs", "Cubbies" } },
        { "Chicago White Sox", new[] { "CWS", "White Sox", "WhiteSox", "ChiSox" } },
        { "Cincinnati Reds", new[] { "CIN Reds", "Reds" } },
        { "Cleveland Guardians", new[] { "CLE Guardians", "Guardians" } },
        { "Colorado Rockies", new[] { "COL", "Rockies" } },
        { "Detroit Tigers", new[] { "DET Tigers", "Tigers" } },
        { "Houston Astros", new[] { "HOU Astros", "Astros" } },
        { "Kansas City Royals", new[] { "KC Royals", "Royals" } },
        { "Los Angeles Angels", new[] { "LAA", "LA Angels", "Angels", "Halos" } },
        { "Los Angeles Dodgers", new[] { "LAD", "LA Dodgers", "Dodgers" } },
        { "Miami Marlins", new[] { "MIA Marlins", "Marlins" } },
        { "Milwaukee Brewers", new[] { "MIL Brewers", "Brewers" } },
        { "Minnesota Twins", new[] { "MIN Twins", "Twins" } },
        { "New York Mets", new[] { "NYM", "NY Mets", "Mets" } },
        { "New York Yankees", new[] { "NYY", "NY Yankees", "Yankees", "Yanks" } },
        { "Oakland Athletics", new[] { "OAK", "Athletics" } },
        { "Philadelphia Phillies", new[] { "PHI Phillies", "Phillies", "Phils" } },
        { "Pittsburgh Pirates", new[] { "PIT Pirates", "Pirates" } },
        { "San Diego Padres", new[] { "SD", "Padres" } },
        { "San Francisco Giants", new[] { "SF Giants", "SFG" } },
        { "Seattle Mariners", new[] { "SEA Mariners", "Mariners" } },
        { "St. Louis Cardinals", new[] { "STL", "STL Cardinals", "Cardinals", "Cards" } },
        { "Tampa Bay Rays", new[] { "TB Rays", "Rays" } },
        { "Texas Rangers", new[] { "TEX", "Rangers" } },
        { "Toronto Blue Jays", new[] { "TOR Blue Jays", "Blue Jays", "BlueJays", "Jays" } },
        { "Washington Nationals", new[] { "WSH", "Nationals", "Nats" } },

        // ============================================================
        // NHL Teams (32)
        // ============================================================
        { "Anaheim Ducks", new[] { "ANA", "Ducks" } },
        { "Arizona Coyotes", new[] { "ARI Coyotes", "Coyotes" } },
        { "Boston Bruins", new[] { "BOS Bruins", "Bruins" } },
        { "Buffalo Sabres", new[] { "BUF Sabres", "Sabres" } },
        { "Calgary Flames", new[] { "CGY", "Flames" } },
        { "Carolina Hurricanes", new[] { "CAR Hurricanes", "Hurricanes", "Canes" } },
        { "Chicago Blackhawks", new[] { "CHI Blackhawks", "Blackhawks", "Hawks" } },
        { "Colorado Avalanche", new[] { "COL Avalanche", "Avalanche", "Avs" } },
        { "Columbus Blue Jackets", new[] { "CBJ", "Blue Jackets" } },
        { "Dallas Stars", new[] { "DAL Stars", "Stars" } },
        { "Detroit Red Wings", new[] { "DET Red Wings", "Red Wings", "RedWings" } },
        { "Edmonton Oilers", new[] { "EDM", "Oilers" } },
        { "Florida Panthers", new[] { "FLA", "FLA Panthers" } },
        { "Los Angeles Kings", new[] { "LAK", "LA Kings", "Kings" } },
        { "Minnesota Wild", new[] { "MIN Wild", "Wild" } },
        { "Montreal Canadiens", new[] { "MTL", "Canadiens", "Habs" } },
        { "Nashville Predators", new[] { "NSH", "Predators", "Preds" } },
        { "New Jersey Devils", new[] { "NJD", "NJ Devils", "Devils" } },
        { "New York Islanders", new[] { "NYI", "NY Islanders", "Islanders", "Isles" } },
        { "New York Rangers", new[] { "NYR", "NY Rangers" } },
        { "Ottawa Senators", new[] { "OTT", "Senators", "Sens" } },
        { "Philadelphia Flyers", new[] { "PHI Flyers", "Flyers" } },
        { "Pittsburgh Penguins", new[] { "PIT Penguins", "Penguins", "Pens" } },
        { "San Jose Sharks", new[] { "SJS", "SJ Sharks", "Sharks" } },
        { "Seattle Kraken", new[] { "SEA Kraken", "Kraken" } },
        { "St. Louis Blues", new[] { "STL Blues", "Blues" } },
        { "Tampa Bay Lightning", new[] { "TBL", "TB Lightning", "Lightning", "Bolts" } },
        { "Toronto Maple Leafs", new[] { "TOR Maple Leafs", "Maple Leafs", "Leafs" } },
        { "Utah Hockey Club", new[] { "UTA Hockey", "Utah HC" } },
        { "Vancouver Canucks", new[] { "VAN", "Canucks" } },
        { "Vegas Golden Knights", new[] { "VGK", "Golden Knights", "Knights" } },
        { "Washington Capitals", new[] { "WSH Capitals", "Capitals", "Caps" } },
        { "Winnipeg Jets", new[] { "WPG", "WPG Jets" } },
    };
}
