import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import QueueDetails from 'Activity/Queue/Details/QueueDetailsProvider';
import AppState from 'App/State/AppState';
import * as commandNames from 'Commands/commandNames';
import FilterMenu from 'Components/Menu/FilterMenu';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import FightCard from 'FightCard/FightCard';
import useMeasure from 'Helpers/Hooks/useMeasure';
import { align, icons } from 'Helpers/Props';
import NoSeries from 'Events/NoEvent';
import {
  setCalendarDaysCount,
  setCalendarFilter,
} from 'Store/Actions/calendarActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { createCustomFiltersSelector } from 'Store/Selectors/createClientSideCollectionSelector';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import createEventCountSelector from 'Store/Selectors/createEventCountSelector';
import selectUniqueIds from 'Utilities/Object/selectUniqueIds';
import translate from 'Utilities/String/translate';
import Calendar from './Calendar';
import CalendarFilterModal from './CalendarFilterModal';
import CalendarMissingEpisodeSearchButton from './CalendarMissingEpisodeSearchButton';
import CalendarLinkModal from './iCal/CalendarLinkModal';
import Legend from './Legend/Legend';
import CalendarOptionsModal from './Options/CalendarOptionsModal';
import styles from './CalendarPage.css';

const MINIMUM_DAY_WIDTH = 120;

function CalendarPage() {
  const dispatch = useDispatch();

  const { selectedFilterKey, filters, items } = useSelector(
    (state: AppState) => state.calendar
  );
  const isRssSyncExecuting = useSelector(
    createCommandExecutingSelector(commandNames.RSS_SYNC)
  );
  const customFilters = useSelector(createCustomFiltersSelector('calendar'));
  const hasSeries = !!useSelector(createEventCountSelector());

  const [pageContentRef, { width }] = useMeasure();
  const [isCalendarLinkModalOpen, setIsCalendarLinkModalOpen] = useState(false);
  const [isOptionsModalOpen, setIsOptionsModalOpen] = useState(false);

  const isMeasured = width > 0;
  const PageComponent = hasSeries ? Calendar : NoSeries;

  const handleGetCalendarLinkPress = useCallback(() => {
    setIsCalendarLinkModalOpen(true);
  }, []);

  const handleGetCalendarLinkModalClose = useCallback(() => {
    setIsCalendarLinkModalOpen(false);
  }, []);

  const handleOptionsPress = useCallback(() => {
    setIsOptionsModalOpen(true);
  }, []);

  const handleOptionsModalClose = useCallback(() => {
    setIsOptionsModalOpen(false);
  }, []);

  const handleRssSyncPress = useCallback(() => {
    dispatch(
      executeCommand({
        name: commandNames.RSS_SYNC,
      })
    );
  }, [dispatch]);

  const handleFilterSelect = useCallback(
    (key: string | number) => {
      dispatch(setCalendarFilter({ selectedFilterKey: key }));
    },
    [dispatch]
  );

  const episodeIds = useMemo(() => {
    return selectUniqueIds<Episode, number>(items, 'id');
  }, [items]);

  useEffect(() => {
    if (width === 0) {
      return;
    }

    const dayCount = Math.max(
      3,
      Math.min(7, Math.floor(width / MINIMUM_DAY_WIDTH))
    );

    dispatch(setCalendarDaysCount({ dayCount }));
  }, [width, dispatch]);

  return (
    <QueueDetails episodeIds={episodeIds}>
      <PageContent title={translate('Calendar')}>
        <PageToolbar>
          <PageToolbarSection>
            <PageToolbarButton
              label={translate('ICalLink')}
              iconName={icons.CALENDAR}
              onPress={handleGetCalendarLinkPress}
            />

            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('RssSync')}
              iconName={icons.RSS}
              isSpinning={isRssSyncExecuting}
              onPress={handleRssSyncPress}
            />

            <CalendarMissingEpisodeSearchButton />
          </PageToolbarSection>

          <PageToolbarSection alignContent={align.RIGHT}>
            <PageToolbarButton
              label={translate('Options')}
              iconName={icons.POSTER}
              onPress={handleOptionsPress}
            />

            <FilterMenu
              alignMenu={align.RIGHT}
              isDisabled={!hasSeries}
              selectedFilterKey={selectedFilterKey}
              filters={filters}
              customFilters={customFilters}
              filterModalConnectorComponent={CalendarFilterModal}
              onFilterSelect={handleFilterSelect}
            />
          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody
          ref={pageContentRef}
          className={styles.calendarPageBody}
          innerClassName={styles.calendarInnerPageBody}
        >
          {isMeasured ? <PageComponent totalItems={0} /> : <div />}
          {hasSeries && <Legend />}
        </PageContentBody>

        <CalendarLinkModal
          isOpen={isCalendarLinkModalOpen}
          onModalClose={handleGetCalendarLinkModalClose}
        />

        <CalendarOptionsModal
          isOpen={isOptionsModalOpen}
          onModalClose={handleOptionsModalClose}
        />
      </PageContent>
    </QueueDetails>
  );
}

export default CalendarPage;
