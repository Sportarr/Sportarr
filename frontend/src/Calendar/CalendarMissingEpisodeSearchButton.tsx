import moment from 'moment';
import React, { useCallback } from 'react';
import { useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import { useQueueDetails } from 'Activity/Queue/Details/QueueDetailsProvider';
import AppState from 'App/State/AppState';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import { icons } from 'Helpers/Props';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import Queue from 'typings/Queue';
import { isCommandExecuting } from 'Utilities/Command';
import isBefore from 'Utilities/Date/isBefore';
import translate from 'Utilities/String/translate';

function createIsSearchingSelector() {
  return createSelector(
    (state: AppState) => state.calendar.searchMissingCommandId,
    createCommandsSelector(),
    (searchMissingCommandId, commands) => {
      if (searchMissingCommandId == null) {
        return false;
      }

      return isCommandExecuting(
        commands.find((command) => {
          return command.id === searchMissingCommandId;
        })
      );
    }
  );
}

function createMissingEpisodeIdsSelector(queueDetails: Queue[]) {
  return createSelector(
    (state: AppState) => state.calendar.start,
    (state: AppState) => state.calendar.end,
    (state: AppState) => state.calendar.items,
    (start, end, episodes) => {
      return episodes.reduce<number[]>((acc, episode) => {
        const airDateUtc = episode.airDateUtc;

        if (
          !episode.episodeFileId &&
          moment(airDateUtc).isAfter(start) &&
          moment(airDateUtc).isBefore(end) &&
          isBefore(episode.airDateUtc) &&
          !queueDetails.some(
            (details) => !!details.episode && details.episode.id === episode.id
          )
        ) {
          acc.push(episode.id);
        }

        return acc;
      }, []);
    }
  );
}

export default function CalendarMissingEpisodeSearchButton() {
  const queueDetails = useQueueDetails();
  const missingEpisodeIds = useSelector(
    createMissingEpisodeIdsSelector(queueDetails)
  );
  const isSearchingForMissing = useSelector(createIsSearchingSelector());

  const handlePress = useCallback(() => {}, []);

  return (
    <PageToolbarButton
      label={translate('SearchForMissing')}
      iconName={icons.SEARCH}
      isDisabled={!missingEpisodeIds.length}
      isSpinning={isSearchingForMissing}
      onPress={handlePress}
    />
  );
}
