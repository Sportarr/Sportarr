import React, { useMemo } from 'react';
import { useSelector } from 'react-redux';
import { IconName } from 'Components/Icon';
import { icons } from 'Helpers/Props';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import dimensions from 'Styles/Variables/dimensions';
import QualityProfile from 'typings/QualityProfile';
import UiSettings from 'typings/Settings/UiSettings';
import formatDateTime from 'Utilities/Date/formatDateTime';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import SeriesIndexOverviewInfoRow from './EventIndexOverviewInfoRow';
import styles from './EventIndexOverviewInfo.css';

interface RowProps {
  name: string;
  showProp: string;
  valueProp: string;
}

interface RowInfoProps {
  title: string;
  iconName: IconName;
  label: string;
}

interface SeriesIndexOverviewInfoProps {
  height: number;
  showOrganization: boolean;
  showMonitored: boolean;
  showQualityProfile: boolean;
  showPreviousEvent: boolean;
  showAdded: boolean;
  showFightCardCount: boolean;
  showPath: boolean;
  showSizeOnDisk: boolean;
  monitored: boolean;
  nextEvent?: string;
  network?: string;
  qualityProfile?: QualityProfile;
  previousEvent?: string;
  added?: string;
  fightCardCount: number;
  path: string;
  sizeOnDisk?: number;
  sortKey: string;
}

const infoRowHeight = parseInt(dimensions.seriesIndexOverviewInfoRowHeight);

const rows = [
  {
    name: 'monitored',
    showProp: 'showMonitored',
    valueProp: 'monitored',
  },
  {
    name: 'network',
    showProp: 'showOrganization',
    valueProp: 'network',
  },
  {
    name: 'qualityProfileId',
    showProp: 'showQualityProfile',
    valueProp: 'qualityProfile',
  },
  {
    name: 'previousEvent',
    showProp: 'showPreviousEvent',
    valueProp: 'previousEvent',
  },
  {
    name: 'added',
    showProp: 'showAdded',
    valueProp: 'added',
  },
  {
    name: 'fightCardCount',
    showProp: 'showFightCardCount',
    valueProp: 'fightCardCount',
  },
  {
    name: 'path',
    showProp: 'showPath',
    valueProp: 'path',
  },
  {
    name: 'sizeOnDisk',
    showProp: 'showSizeOnDisk',
    valueProp: 'sizeOnDisk',
  },
];

function getInfoRowProps(
  row: RowProps,
  props: SeriesIndexOverviewInfoProps,
  uiSettings: UiSettings
): RowInfoProps | null {
  const { name } = row;

  if (name === 'monitored') {
    const monitoredText = props.monitored
      ? translate('Monitored')
      : translate('Unmonitored');

    return {
      title: monitoredText,
      iconName: props.monitored ? icons.MONITORED : icons.UNMONITORED,
      label: monitoredText,
    };
  }

  if (name === 'network') {
    return {
      title: translate('Network'),
      iconName: icons.NETWORK,
      label: props.network ?? '',
    };
  }

  if (name === 'qualityProfileId' && !!props.qualityProfile?.name) {
    return {
      title: translate('QualityProfile'),
      iconName: icons.PROFILE,
      label: props.qualityProfile.name,
    };
  }

  if (name === 'previousEvent') {
    const previousEvent = props.previousEvent;
    const { showRelativeDates, shortDateFormat, longDateFormat, timeFormat } =
      uiSettings;

    return {
      title: translate('PreviousAiringDate', {
        date: formatDateTime(previousEvent, longDateFormat, timeFormat),
      }),
      iconName: icons.CALENDAR,
      label: getRelativeDate({
        date: previousEvent,
        shortDateFormat,
        showRelativeDates,
        timeFormat,
        timeForToday: true,
      }),
    };
  }

  if (name === 'added') {
    const added = props.added;
    const { showRelativeDates, shortDateFormat, longDateFormat, timeFormat } =
      uiSettings;

    return {
      title: translate('AddedDate', {
        date: formatDateTime(added, longDateFormat, timeFormat),
      }),
      iconName: icons.ADD,
      label:
        getRelativeDate({
          date: added,
          shortDateFormat,
          showRelativeDates,
          timeFormat,
          timeForToday: true,
        }) ?? '',
    };
  }

  if (name === 'fightCardCount') {
    const { fightCardCount } = props;
    let seasons = translate('OneSeason');

    if (fightCardCount === 0) {
      seasons = translate('NoSeasons');
    } else if (fightCardCount > 1) {
      seasons = translate('CountSeasons', { count: fightCardCount });
    }

    return {
      title: translate('SeasonCount'),
      iconName: icons.CIRCLE,
      label: seasons,
    };
  }

  if (name === 'path') {
    return {
      title: translate('Path'),
      iconName: icons.FOLDER,
      label: props.path,
    };
  }

  if (name === 'sizeOnDisk') {
    const { sizeOnDisk = 0 } = props;

    return {
      title: translate('SizeOnDisk'),
      iconName: icons.DRIVE,
      label: formatBytes(sizeOnDisk),
    };
  }

  return null;
}

function SeriesIndexOverviewInfo(props: SeriesIndexOverviewInfoProps) {
  const { height, nextEvent } = props;

  const uiSettings = useSelector(createUISettingsSelector());

  const { shortDateFormat, showRelativeDates, longDateFormat, timeFormat } =
    uiSettings;

  let shownRows = 1;
  const maxRows = Math.floor(height / (infoRowHeight + 4));

  const rowInfo = useMemo(() => {
    return rows.map((row) => {
      const { name, showProp, valueProp } = row;

      const isVisible =
        // eslint-disable-next-line @typescript-eslint/ban-ts-comment
        // @ts-ignore ts(7053)
        props[valueProp] != null && (props[showProp] || props.sortKey === name);

      return {
        ...row,
        isVisible,
      };
    });
  }, [props]);

  return (
    <div className={styles.infos}>
      {!!nextEvent && (
        <SeriesIndexOverviewInfoRow
          title={translate('NextAiringDate', {
            date: formatDateTime(nextEvent, longDateFormat, timeFormat),
          })}
          iconName={icons.SCHEDULED}
          label={getRelativeDate({
            date: nextEvent,
            shortDateFormat,
            showRelativeDates,
            timeFormat,
            timeForToday: true,
          })}
        />
      )}

      {rowInfo.map((row) => {
        if (!row.isVisible) {
          return null;
        }

        if (shownRows >= maxRows) {
          return null;
        }

        shownRows++;

        const infoRowProps = getInfoRowProps(row, props, uiSettings);

        if (infoRowProps == null) {
          return null;
        }

        return <SeriesIndexOverviewInfoRow key={row.name} {...infoRowProps} />;
      })}
    </div>
  );
}

export default SeriesIndexOverviewInfo;
