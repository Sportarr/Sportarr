import React, { useCallback } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { OptionChanged } from 'Helpers/Hooks/useOptionsStore';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import {
  QueueOptions as QueueOptionsType,
  setQueueOption,
  useQueueOption,
} from './queueOptionsStore';
import useQueue from './useQueue';

function QueueOptions() {
  const includeUnknownSeriesItems = useQueueOption('includeUnknownSeriesItems');
  const { goToPage } = useQueue();

  const handleOptionChange = useCallback(
    ({ name, value }: OptionChanged<QueueOptionsType>) => {
      setQueueOption(name, value);

      if (name === 'includeUnknownSeriesItems') {
        goToPage(1);
      }
    },
    [goToPage]
  );

  return (
    <FormGroup>
      <FormLabel>{translate('ShowUnknownSeriesItems')}</FormLabel>

      <FormInputGroup
        type={inputTypes.CHECK}
        name="includeUnknownSeriesItems"
        value={includeUnknownSeriesItems}
        helpText={translate('ShowUnknownSeriesItemsHelpText')}
        // @ts-expect-error - The typing for inputs needs more work
        onChange={handleOptionChange}
      />
    </FormGroup>
  );
}

export default QueueOptions;
