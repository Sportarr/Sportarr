import { useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import FightCard from './FightCard';

export type FightCardEntity =
  | 'calendar'
  | 'episodes'
  | 'interactiveImport.episodes'
  | 'wanted.cutoffUnmet'
  | 'wanted.missing';

// Legacy alias
export type EpisodeEntity = FightCardEntity;

function createEpisodeSelector(episodeId?: number) {
  return createSelector(
    (state: AppState) => state.episodes.items,
    (episodes) => {
      return episodes.find(({ id }) => id === episodeId);
    }
  );
}

function createCalendarEpisodeSelector(episodeId?: number) {
  return createSelector(
    (state: AppState) => state.calendar.items as FightCard[],
    (episodes) => {
      return episodes.find(({ id }) => id === episodeId);
    }
  );
}

function createWantedCutoffUnmetEpisodeSelector(episodeId?: number) {
  return createSelector(
    (state: AppState) => state.wanted.cutoffUnmet.items,
    (episodes) => {
      return episodes.find(({ id }) => id === episodeId);
    }
  );
}

function createWantedMissingEpisodeSelector(episodeId?: number) {
  return createSelector(
    (state: AppState) => state.wanted.missing.items,
    (episodes) => {
      return episodes.find(({ id }) => id === episodeId);
    }
  );
}

function useEpisode(
  episodeId: number | undefined,
  episodeEntity: EpisodeEntity
) {
  let selector = createEpisodeSelector;

  switch (episodeEntity) {
    case 'calendar':
      selector = createCalendarEpisodeSelector;
      break;
    case 'wanted.cutoffUnmet':
      selector = createWantedCutoffUnmetEpisodeSelector;
      break;
    case 'wanted.missing':
      selector = createWantedMissingEpisodeSelector;
      break;
    default:
      break;
  }

  return useSelector(selector(episodeId));
}

export default useEpisode;
