import React, { useCallback, useState } from 'react';
import Modal from 'Components/Modal/Modal';
import FightCardDetailsTab from 'FightCard/FightCardDetailsTab';
import { FightCardEntity } from 'FightCard/useFightCard';
import { sizes } from 'Helpers/Props';
import EpisodeDetailsModalContent from './FightCardDetailsModalContent';

interface EpisodeDetailsModalProps {
  isOpen: boolean;
  episodeId: number;
  episodeEntity: FightCardEntity;
  seriesId: number;
  episodeTitle: string;
  isSaving?: boolean;
  showOpenSeriesButton?: boolean;
  selectedTab?: FightCardDetailsTab;
  startInteractiveSearch?: boolean;
  onModalClose(): void;
}

function EpisodeDetailsModal(props: EpisodeDetailsModalProps) {
  const { selectedTab, isOpen, onModalClose, ...otherProps } = props;

  const [closeOnBackgroundClick, setCloseOnBackgroundClick] = useState(
    selectedTab !== 'search'
  );

  const handleTabChange = useCallback(
    (isSearch: boolean) => {
      setCloseOnBackgroundClick(!isSearch);
    },
    [setCloseOnBackgroundClick]
  );

  return (
    <Modal
      isOpen={isOpen}
      size={sizes.EXTRA_EXTRA_LARGE}
      closeOnBackgroundClick={closeOnBackgroundClick}
      onModalClose={onModalClose}
    >
      <EpisodeDetailsModalContent
        {...otherProps}
        selectedTab={selectedTab}
        onTabChange={handleTabChange}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default EpisodeDetailsModal;
