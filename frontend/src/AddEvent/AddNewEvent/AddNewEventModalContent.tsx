import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useSelector } from 'react-redux';
import AddEvent from 'AddEvent/AddEvent';
import {
  AddEventOptions,
  setAddEventOption,
  useAddEventOptions,
} from 'AddEvent/addEventOptionsStore';
import SeriesMonitoringOptionsPopoverContent from 'AddEvent/SeriesMonitoringOptionsPopoverContent';
import SeriesTypePopoverContent from 'AddEvent/SeriesTypePopoverContent';
import CheckInput from 'Components/Form/CheckInput';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Icon from 'Components/Icon';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Popover from 'Components/Tooltip/Popover';
import { icons, inputTypes, kinds, tooltipPositions } from 'Helpers/Props';
import { SeriesType } from 'Events/Event';
import SeriesPoster from 'Events/EventPoster';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import selectSettings from 'Store/Selectors/selectSettings';
import useIsWindows from 'System/useIsWindows';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { useAddEvent } from './useAddEvent';
import styles from './AddNewEventModalContent.css';

export interface AddNewSeriesModalContentProps {
  event: AddEvent;
  initialSeriesType: SeriesType;
  onModalClose: () => void;
}

function AddNewSeriesModalContent({
  event,
  initialSeriesType,
  onModalClose,
}: AddNewSeriesModalContentProps) {
  const { title, year, overview, images, folder } = event;
  const options = useAddEventOptions();
  const { isSmallScreen } = useSelector(createDimensionsSelector());
  const isWindows = useIsWindows();

  const { isAdding, addError, addSeries } = useAddEvent();

  const { settings, validationErrors, validationWarnings } = useMemo(() => {
    return selectSettings(options, {}, addError);
  }, [options, addError]);

  const [seriesType, setSeriesType] = useState<SeriesType>(
    initialSeriesType === 'standard'
      ? settings.seriesType.value
      : initialSeriesType
  );

  const {
    monitor,
    qualityProfileId,
    rootFolderPath,
    searchForCutoffUnmetEpisodes,
    searchForMissingEpisodes,
    seasonFolder,
    seriesType: seriesTypeSetting,
    tags,
  } = settings;

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged<string | number | boolean | number[]>) => {
      setAddEventOption(name as keyof AddEventOptions, value);
    },
    []
  );

  const handleQualityProfileIdChange = useCallback(
    ({ value }: InputChanged<string | number>) => {
      setAddEventOption('qualityProfileId', value as number);
    },
    []
  );

  const handleAddEventPress = useCallback(() => {
    addSeries({
      ...event,
      rootFolderPath: rootFolderPath.value,
      monitor: monitor.value,
      qualityProfileId: qualityProfileId.value,
      seriesType,
      seasonFolder: seasonFolder.value,
      searchForMissingEpisodes: searchForMissingEpisodes.value,
      searchForCutoffUnmetEpisodes: searchForCutoffUnmetEpisodes.value,
      tags: tags.value,
    });
  }, [
    event,
    seriesType,
    rootFolderPath,
    monitor,
    qualityProfileId,
    seasonFolder,
    searchForMissingEpisodes,
    searchForCutoffUnmetEpisodes,
    tags,
    addSeries,
  ]);

  useEffect(() => {
    setSeriesType(seriesTypeSetting.value);
  }, [seriesTypeSetting]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {title}

        {!title.includes(String(year)) && year ? (
          <span className={styles.year}>({year})</span>
        ) : null}
      </ModalHeader>

      <ModalBody>
        <div className={styles.container}>
          {isSmallScreen ? null : (
            <div className={styles.poster}>
              <SeriesPoster
                className={styles.poster}
                images={images}
                size={250}
              />
            </div>
          )}

          <div className={styles.info}>
            {overview ? (
              <div className={styles.overview}>{overview}</div>
            ) : null}

            <Form
              validationErrors={validationErrors}
              validationWarnings={validationWarnings}
            >
              <FormGroup>
                <FormLabel>{translate('RootFolder')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.ROOT_FOLDER_SELECT}
                  name="rootFolderPath"
                  valueOptions={{
                    seriesFolder: folder,
                    isWindows,
                  }}
                  selectedValueOptions={{
                    seriesFolder: folder,
                    isWindows,
                  }}
                  helpText={translate('AddNewSeriesRootFolderHelpText', {
                    folder,
                  })}
                  onChange={handleInputChange}
                  {...rootFolderPath}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  {translate('Monitor')}

                  <Popover
                    anchor={
                      <Icon className={styles.labelIcon} name={icons.INFO} />
                    }
                    title={translate('MonitoringOptions')}
                    body={<SeriesMonitoringOptionsPopoverContent />}
                    position={tooltipPositions.RIGHT}
                  />
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.MONITOR_EPISODES_SELECT}
                  name="monitor"
                  onChange={handleInputChange}
                  {...monitor}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('QualityProfile')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.QUALITY_PROFILE_SELECT}
                  name="qualityProfileId"
                  onChange={handleQualityProfileIdChange}
                  {...qualityProfileId}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  {translate('SeriesType')}

                  <Popover
                    anchor={
                      <Icon className={styles.labelIcon} name={icons.INFO} />
                    }
                    title={translate('SeriesTypes')}
                    body={<SeriesTypePopoverContent />}
                    position={tooltipPositions.RIGHT}
                  />
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.SERIES_TYPE_SELECT}
                  name="seriesType"
                  onChange={handleInputChange}
                  {...seriesTypeSetting}
                  value={seriesType}
                  helpText={translate('SeriesTypesHelpText')}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('SeasonFolder')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="seasonFolder"
                  onChange={handleInputChange}
                  {...seasonFolder}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('Tags')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.TAG}
                  name="tags"
                  onChange={handleInputChange}
                  {...tags}
                />
              </FormGroup>
            </Form>
          </div>
        </div>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div>
          <label className={styles.searchLabelContainer}>
            <span className={styles.searchLabel}>
              {translate('AddNewSeriesSearchForMissingEpisodes')}
            </span>

            <CheckInput
              containerClassName={styles.searchInputContainer}
              className={styles.searchInput}
              name="searchForMissingEpisodes"
              onChange={handleInputChange}
              {...searchForMissingEpisodes}
            />
          </label>

          <label className={styles.searchLabelContainer}>
            <span className={styles.searchLabel}>
              {translate('AddNewSeriesSearchForCutoffUnmetEpisodes')}
            </span>

            <CheckInput
              containerClassName={styles.searchInputContainer}
              className={styles.searchInput}
              name="searchForCutoffUnmetEpisodes"
              onChange={handleInputChange}
              {...searchForCutoffUnmetEpisodes}
            />
          </label>
        </div>

        <SpinnerButton
          className={styles.addButton}
          kind={kinds.SUCCESS}
          isSpinning={isAdding}
          onPress={handleAddEventPress}
        >
          {translate('AddEventWithTitle', { title })}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddNewSeriesModalContent;
