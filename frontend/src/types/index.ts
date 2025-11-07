export interface Event {
  id: number;
  title: string;
  organization: string;
  eventDate: string;
  venue?: string;
  location?: string;
  monitored: boolean;
  hasFile: boolean;
  images: Image[];
  quality?: string;
  qualityProfileId?: number;
  filePath?: string;
  fileSize?: number;
  fightCards?: FightCard[];
  tags?: number[];
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
