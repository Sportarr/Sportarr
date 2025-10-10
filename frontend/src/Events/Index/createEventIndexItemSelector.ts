import { maxBy } from 'lodash';
import { createSelector } from 'reselect';
import Command from 'Commands/Command';
import { REFRESH_SERIES, SERIES_SEARCH } from 'Commands/commandNames';
import Event from 'Events/Event';
import createExecutingCommandsSelector from 'Store/Selectors/createExecutingCommandsSelector';
import createSeriesQualityProfileSelector from 'Store/Selectors/createEventQualityProfileSelector';
import { createSeriesSelectorForHook } from 'Store/Selectors/createEventSelector';

function createSeriesIndexItemSelector(seriesId: number) {
  return createSelector(
    createSeriesSelectorForHook(seriesId),
    createSeriesQualityProfileSelector(seriesId),
    createExecutingCommandsSelector(),
    (event: Event, qualityProfile, executingCommands: Command[]) => {
      const isRefreshingSeries = executingCommands.some((command) => {
        return (
          command.name === REFRESH_SERIES &&
          command.body.seriesIds?.includes(event.id)
        );
      });

      const isSearchingSeries = executingCommands.some((command) => {
        return (
          command.name === SERIES_SEARCH && command.body.seriesId === seriesId
        );
      });

      const latestSeason = maxBy(
        event.seasons,
        (card) => card.seasonNumber
      );

      return {
        event,
        qualityProfile,
        latestSeason,
        isRefreshingSeries,
        isSearchingSeries,
      };
    }
  );
}

export default createSeriesIndexItemSelector;
