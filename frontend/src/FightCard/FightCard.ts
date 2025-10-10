import ModelBase from 'App/ModelBase';
import Event from 'Event/Event';

interface FightCard extends ModelBase {
  seriesId: number;
  tvdbId: number;
  episodeFileId: number;
  seasonNumber: number;
  episodeNumber: number;
  airDate: string;
  airDateUtc?: string;
  lastSearchTime?: string;
  runtime: number;
  absoluteEpisodeNumber?: number;
  sceneSeasonNumber?: number;
  sceneEpisodeNumber?: number;
  sceneAbsoluteEpisodeNumber?: number;
  overview: string;
  title: string;
  episodeFile?: object;
  hasFile: boolean;
  monitored: boolean;
  grabbed?: boolean;
  unverifiedSceneNumbering: boolean;
  event?: Event;
  finaleType?: string;
}

export default FightCard;
