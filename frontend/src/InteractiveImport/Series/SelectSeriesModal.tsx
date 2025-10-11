import React from 'react';
import Modal from 'Components/Modal/Modal';
import Event from 'Events/Event';
import SelectSeriesModalContent from './SelectSeriesModalContent';

interface SelectSeriesModalProps {
  isOpen: boolean;
  modalTitle: string;
  onSeriesSelect(event: Event): void;
  onModalClose(): void;
}

function SelectSeriesModal(props: SelectSeriesModalProps) {
  const { isOpen, modalTitle, onSeriesSelect, onModalClose } = props;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <SelectSeriesModalContent
        modalTitle={modalTitle}
        onSeriesSelect={onSeriesSelect}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default SelectSeriesModal;
