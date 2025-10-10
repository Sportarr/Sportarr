import React, { useMemo } from 'react';
import { Card } from 'Events/Event';
import translate from 'Utilities/String/translate';
import SeasonPassSeason from './SeasonPassSeason';
import styles from './SeasonDetails.css';

interface SeasonDetailsProps {
  seriesId: number;
  seasons: Card[];
}

function SeasonDetails(props: SeasonDetailsProps) {
  const { seriesId, seasons } = props;

  const latestSeasons = useMemo(() => {
    return seasons.slice(Math.max(seasons.length - 25, 0));
  }, [seasons]);

  return (
    <div className={styles.seasons}>
      {latestSeasons.map((card) => {
        const {
          seasonNumber,
          monitored,
          statistics,
          isSaving = false,
        } = card;

        return (
          <SeasonPassSeason
            key={seasonNumber}
            seriesId={seriesId}
            seasonNumber={seasonNumber}
            monitored={monitored}
            statistics={statistics}
            isSaving={isSaving}
          />
        );
      })}

      {latestSeasons.length < seasons.length ? (
        <div className={styles.truncated}>
          {translate('SeasonPassTruncated')}
        </div>
      ) : null}
    </div>
  );
}

export default SeasonDetails;
