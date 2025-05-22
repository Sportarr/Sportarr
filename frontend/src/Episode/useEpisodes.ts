import { useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import Episode from './Episode';

export type EpisodeEntity =
  | 'calendar'
  | 'episodes'
  | 'interactiveImport.episodes'
  | 'wanted.cutoffUnmet'
  | 'wanted.missing';

function getEpisodes(episodeIds: number[], episodes: Episode[]) {
  return episodeIds.reduce<Episode[]>((acc, id) => {
    const episode = episodes.find((episode) => episode.id === id);

    if (episode) {
      acc.push(episode);
    }

    return acc;
  }, []);
}

function createEpisodeSelector(episodeIds: number[]) {
  return createSelector(
    (state: AppState) => state.episodes.items,
    (episodes) => {
      return getEpisodes(episodeIds, episodes);
    }
  );
}

function createCalendarEpisodeSelector(episodeIds: number[]) {
  return createSelector(
    (state: AppState) => state.calendar.items as Episode[],
    (episodes) => {
      return getEpisodes(episodeIds, episodes);
    }
  );
}

function createWantedCutoffUnmetEpisodeSelector(episodeIds: number[]) {
  return createSelector(
    (state: AppState) => state.wanted.cutoffUnmet.items,
    (episodes) => {
      return getEpisodes(episodeIds, episodes);
    }
  );
}

function createWantedMissingEpisodeSelector(episodeIds: number[]) {
  return createSelector(
    (state: AppState) => state.wanted.missing.items,
    (episodes) => {
      return getEpisodes(episodeIds, episodes);
    }
  );
}

export default function useEpisodes(
  episodeIds: number[],
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

  return useSelector(selector(episodeIds));
}
