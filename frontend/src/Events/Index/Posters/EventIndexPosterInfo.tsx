import React from 'react';
import SeriesTagList from 'Components/SeriesTagList';
import Language from 'Language/Language';
import QualityProfile from 'typings/QualityProfile';
import formatDateTime from 'Utilities/Date/formatDateTime';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './EventIndexPosterInfo.css';

interface SeriesIndexPosterInfoProps {
  originalLanguage?: Language;
  network?: string;
  showQualityProfile: boolean;
  qualityProfile?: QualityProfile;
  previousEvent?: string;
  added?: string;
  fightCardCount: number;
  path: string;
  sizeOnDisk?: number;
  tags: number[];
  sortKey: string;
  showRelativeDates: boolean;
  shortDateFormat: string;
  longDateFormat: string;
  timeFormat: string;
  showTags: boolean;
}

function SeriesIndexPosterInfo(props: SeriesIndexPosterInfoProps) {
  const {
    originalLanguage,
    network,
    qualityProfile,
    showQualityProfile,
    previousEvent,
    added,
    fightCardCount,
    path,
    sizeOnDisk = 0,
    tags,
    sortKey,
    showRelativeDates,
    shortDateFormat,
    longDateFormat,
    timeFormat,
    showTags,
  } = props;

  if (sortKey === 'network' && network) {
    return (
      <div className={styles.info} title={translate('Network')}>
        {network}
      </div>
    );
  }

  if (sortKey === 'originalLanguage' && !!originalLanguage?.name) {
    return (
      <div className={styles.info} title={translate('OriginalLanguage')}>
        {originalLanguage.name}
      </div>
    );
  }

  if (
    sortKey === 'qualityProfileId' &&
    !showQualityProfile &&
    !!qualityProfile?.name
  ) {
    return (
      <div className={styles.info} title={translate('QualityProfile')}>
        {qualityProfile.name}
      </div>
    );
  }

  if (sortKey === 'previousEvent' && previousEvent) {
    return (
      <div
        className={styles.info}
        title={`${translate('PreviousAiring')}: ${formatDateTime(
          previousEvent,
          longDateFormat,
          timeFormat
        )}`}
      >
        {getRelativeDate({
          date: previousEvent,
          shortDateFormat,
          showRelativeDates,
          timeFormat,
          timeForToday: true,
        })}
      </div>
    );
  }

  if (sortKey === 'added' && added) {
    const addedDate = getRelativeDate({
      date: added,
      shortDateFormat,
      showRelativeDates,
      timeFormat,
      timeForToday: false,
    });

    return (
      <div
        className={styles.info}
        title={formatDateTime(added, longDateFormat, timeFormat)}
      >
        {translate('Added')}: {addedDate}
      </div>
    );
  }

  if (sortKey === 'fightCardCount') {
    let seasons = translate('OneSeason');

    if (fightCardCount === 0) {
      seasons = translate('NoSeasons');
    } else if (fightCardCount > 1) {
      seasons = translate('CountSeasons', { count: fightCardCount });
    }

    return <div className={styles.info}>{seasons}</div>;
  }

  if (!showTags && sortKey === 'tags' && tags.length) {
    return (
      <div className={styles.tags}>
        <div className={styles.tagsList}>
          <SeriesTagList tags={tags} />
        </div>
      </div>
    );
  }

  if (sortKey === 'path') {
    return (
      <div className={styles.info} title={translate('Path')}>
        {path}
      </div>
    );
  }

  if (sortKey === 'sizeOnDisk') {
    return (
      <div className={styles.info} title={translate('SizeOnDisk')}>
        {formatBytes(sizeOnDisk)}
      </div>
    );
  }

  return null;
}

export default SeriesIndexPosterInfo;
