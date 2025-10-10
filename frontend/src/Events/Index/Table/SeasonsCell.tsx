import React from 'react';
import VirtualTableRowCell from 'Components/Table/Cells/VirtualTableRowCell';
import Popover from 'Components/Tooltip/Popover';
import SeasonDetails from 'Events/Index/Select/SeasonPass/SeasonDetails';
import { Card } from 'Events/Event';
import translate from 'Utilities/String/translate';
import styles from './SeasonsCell.css';

interface SeriesStatusCellProps {
  className: string;
  seriesId: number;
  fightCardCount: number;
  seasons: Card[];
  isSelectMode: boolean;
}

function SeasonsCell(props: SeriesStatusCellProps) {
  const {
    className,
    seriesId,
    fightCardCount,
    seasons,
    isSelectMode,
    ...otherProps
  } = props;

  return (
    <VirtualTableRowCell className={className} {...otherProps}>
      {isSelectMode ? (
        <Popover
          className={styles.fightCardCount}
          anchor={fightCardCount}
          title={translate('SeasonDetails')}
          body={<SeasonDetails seriesId={seriesId} seasons={seasons} />}
          position="left"
        />
      ) : (
        fightCardCount
      )}
    </VirtualTableRowCell>
  );
}

export default SeasonsCell;
