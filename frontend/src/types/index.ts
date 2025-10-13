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
