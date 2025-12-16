export interface Event {
  id: number;
  externalId?: string; // TheSportsDB event ID
  title: string;
  organization?: string; // Deprecated - use leagueId instead
  sport: string; // Sport type (e.g., "Soccer", "Fighting", "Basketball")
  leagueId?: number; // League/competition ID
  league?: League; // League details
  leagueName?: string; // League name (from EventResponse)
  homeTeamId?: number; // Home team (for team sports)
  homeTeam?: Team; // Home team details
  homeTeamName?: string; // Home team name (from EventResponse)
  awayTeamId?: number; // Away team (for team sports)
  awayTeam?: Team; // Away team details
  awayTeamName?: string; // Away team name (from EventResponse)
  season?: string; // Season identifier (e.g., "2024", "2024-25")
  round?: string; // Round/week number (e.g., "Week 10", "Quarterfinals")
  eventDate: string;
  venue?: string;
  location?: string;
  broadcast?: string; // TV broadcast information (network, channel)
  monitored: boolean;
  monitoredParts?: string; // Comma-separated list of monitored parts (e.g., "Prelims,Main Card")
  hasFile: boolean;
  images: Image[] | string[];
  quality?: string;
  qualityProfileId?: number;
  filePath?: string;
  fileSize?: number;
  files?: EventFile[]; // Files associated with this event
  partStatuses?: PartStatus[]; // Part-level status for multi-part episodes
  fightCards?: FightCard[]; // DEPRECATED: Use partStatuses instead - kept for backwards compatibility
  tags?: number[];
  inLibrary?: boolean;
  // Team sports specific
  homeScore?: string; // Final home team score (API returns as string)
  awayScore?: string; // Final away team score (API returns as string)
  status?: string; // Event status (Scheduled, Live, Completed, Postponed, Cancelled)
}

/**
 * Status of a specific part for multi-part episodes (fighting sports)
 * Matches the backend PartStatus class
 */
export interface PartStatus {
  partName: string;
  partNumber: number;
  monitored: boolean;
  downloaded: boolean;
  file?: EventFile;
}

/**
 * File associated with an event
 * Matches the backend EventFileResponse class
 */
export interface EventFile {
  id: number;
  filePath: string;
  size: number;
  quality?: string;
  qualityScore: number;
  customFormatScore: number;
  codec?: string;
  source?: string;
  partName?: string;
  partNumber?: number;
  added: string;
  exists: boolean;
}

// DEPRECATED: Use PartStatus instead - kept for backwards compatibility
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
  // Download progress stats
  downloadedMonitoredCount?: number; // Monitored events that have files
  missingCount?: number; // Monitored events missing files
  progressPercent?: number; // 0-100 download completion
  progressStatus?: 'complete' | 'continuing' | 'partial' | 'missing' | 'unmonitored';
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

// Global window type extension for Sportarr config
// Note: The canonical Window.Sportarr declaration is in src/api/client.ts
// This interface is for type reference only
export interface SportarrConfig {
  apiRoot: string;
  apiKey: string;
  urlBase: string;
  version: string;
  instanceName?: string;
}
