export interface Event {
  id: number;
  externalId?: string; // TheSportsDB event ID
  title: string;
  organization?: string; // Deprecated - use leagueId instead
  sport: string; // Sport type (e.g., "Soccer", "Fighting", "Basketball")
  leagueId?: number; // League/competition ID
  league?: League; // League details
  homeTeamId?: number; // Home team (for team sports)
  homeTeam?: Team; // Home team details
  awayTeamId?: number; // Away team (for team sports)
  awayTeam?: Team; // Away team details
  season?: string; // Season identifier (e.g., "2024", "2024-25")
  round?: string; // Round/week number (e.g., "Week 10", "Quarterfinals")
  eventDate: string;
  venue?: string;
  location?: string;
  broadcast?: string; // TV broadcast information (network, channel)
  monitored: boolean;
  hasFile: boolean;
  images: Image[] | string[];
  quality?: string;
  qualityProfileId?: number;
  filePath?: string;
  fileSize?: number;
  fightCards?: FightCard[];
  tags?: number[];
  inLibrary?: boolean;
  // Team sports specific
  homeScore?: string; // Final home team score (API returns as string)
  awayScore?: string; // Final away team score (API returns as string)
  status?: string; // Event status (Scheduled, Live, Completed, Postponed, Cancelled)
}

export interface FightCard {
  id: number;
  eventId: number;
  cardType: string;
  cardNumber: number;
  monitored: boolean;
  qualityProfileId?: number;
  hasFile: boolean;
  filePath?: string;
  fileSize?: number;
  quality?: string;
  airDate?: string;
  inLibrary?: boolean;
}

export interface Image {
  coverType: string;
  url: string;
  remoteUrl: string;
}

export interface SystemStatus {
  appName: string;
  version: string;
  buildTime: string;
  isDebug: boolean;
  isProduction: boolean;
  isDocker: boolean;
  runtimeVersion: string;
  databaseType: string;
  databaseVersion: string;
  authentication: string;
  startTime: string;
  appData: string;
  osName: string;
  osVersion: string;
  branch: string;
  migrationVersion: number;
  urlBase: string;
}

export interface Tag {
  id: number;
  label: string;
}

export interface QualityProfile {
  id: number;
  name: string;
  cutoff: number;
  items: QualityProfileItem[];
  isDefault?: boolean;
}

export interface QualityProfileItem {
  id: number;
  quality: Quality;
  items: QualityProfileItem[];
  allowed: boolean;
}

export interface Quality {
  id: number;
  name: string;
  source: string;
  resolution: number;
}

export interface Indexer {
  id: number;
  name: string;
  implementation: string;
  enable: boolean;
  enableRss?: boolean;
  enableAutomaticSearch?: boolean;
  enableInteractiveSearch?: boolean;
  priority: number;
  fields: IndexerField[];
}

export interface IndexerField {
  name: string;
  value: string | string[];
}

// LEGACY: Kept for backwards compatibility with combat sports
export interface Organization {
  name: string;
  monitored: boolean; // Organization-level monitored status
  qualityProfileId?: number;
  posterUrl?: string;
  eventCount?: number;
  monitoredCount?: number; // Count of monitored events
  fileCount?: number;
  nextEvent?: {
    title: string;
    eventDate: string;
  };
  latestEvent?: {
    id: number;
    title: string;
    eventDate: string;
  };
}

// UNIVERSAL SPORTS: Replaces Organization for all sports
export interface League {
  id: number;
  externalId?: string; // TheSportsDB league ID
  name: string;
  sport: string; // Sport type (e.g., "Soccer", "Fighting", "Basketball")
  country?: string;
  description?: string;
  monitored: boolean;
  qualityProfileId?: number;
  logoUrl?: string;
  bannerUrl?: string;
  posterUrl?: string;
  website?: string;
  formedYear?: number;
  added: string;
  lastUpdate?: string;
  // Stats (populated by backend)
  eventCount?: number;
  monitoredEventCount?: number;
  fileCount?: number;
}

export interface Team {
  id: number;
  externalId?: string; // TheSportsDB team ID
  name: string;
  shortName?: string; // Team abbreviation (e.g., "LAL", "NE")
  alternateName?: string;
  leagueId?: number;
  league?: {
    name: string;
    sport: string;
  };
  sport: string;
  country?: string;
  stadium?: string;
  stadiumLocation?: string;
  stadiumCapacity?: number;
  description?: string;
  badgeUrl?: string; // Team logo/badge
  jerseyUrl?: string; // Team kit/jersey
  bannerUrl?: string;
  website?: string;
  formedYear?: number;
  primaryColor?: string; // Hex color code
  secondaryColor?: string; // Hex color code
  added: string;
  lastUpdate?: string;
  // Stats (populated by backend)
  homeEventCount?: number;
  awayEventCount?: number;
  totalEventCount?: number;
}

export interface Player {
  id: number;
  externalId?: string; // TheSportsDB player ID
  name: string;
  firstName?: string;
  lastName?: string;
  nickname?: string;
  sport: string;
  teamId?: number;
  team?: Team;
  position?: string; // Position (e.g., "Forward", "Quarterback", "Fighter")
  nationality?: string;
  birthDate?: string;
  birthplace?: string;
  height?: number; // Height in cm
  weight?: number; // Weight in kg
  number?: string; // Jersey/uniform number
  description?: string;
  photoUrl?: string; // Headshot
  actionPhotoUrl?: string;
  bannerUrl?: string;
  dominance?: string; // Preferred foot/stance (e.g., "Right", "Left", "Orthodox", "Southpaw")
  website?: string;
  socialMedia?: string;
  // Combat sports specific
  weightClass?: string;
  record?: string; // Fight record (e.g., "20-5-0")
  stance?: string; // Fighting stance
  reach?: number; // Reach in cm
  added: string;
  lastUpdate?: string;
}
