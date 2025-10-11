import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import Event from 'Events/Event';
import QualityProfile from 'typings/QualityProfile';
import { createSeriesSelectorForHook } from './createEventSelector';

function createSeriesQualityProfileSelector(seriesId: number) {
  return createSelector(
    (state: AppState) => state.settings.qualityProfiles.items,
    createSeriesSelectorForHook(seriesId),
    (qualityProfiles: QualityProfile[], series = {} as Event) => {
      return qualityProfiles.find(
        (profile) => profile.id === series.qualityProfileId
      );
    }
  );
}

export default createSeriesQualityProfileSelector;
