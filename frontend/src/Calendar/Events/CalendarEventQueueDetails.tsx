import React from 'react';
import QueueDetails from 'Activity/Queue/QueueDetails';
import CircularProgressBar from 'Components/CircularProgressBar';
import {
  QueueTrackedDownloadState,
  QueueTrackedDownloadStatus,
  StatusMessage,
} from 'typings/Queue';

interface CalendarEventQueueDetailsProps {
  title: string;
  size: number;
  sizeLeft: number;
  estimatedCompletionTime?: string;
  status: string;
  trackedDownloadState: QueueTrackedDownloadState;
  trackedDownloadStatus: QueueTrackedDownloadStatus;
  statusMessages?: StatusMessage[];
  errorMessage?: string;
}

function CalendarEventQueueDetails({
  title,
  size,
  sizeLeft,
  estimatedCompletionTime,
  status,
  trackedDownloadState,
  trackedDownloadStatus,
  statusMessages,
  errorMessage,
}: CalendarEventQueueDetailsProps) {
  const progress = size ? 100 - (sizeLeft / size) * 100 : 0;

  return (
    <QueueDetails
      title={title}
      size={size}
      sizeLeft={sizeLeft}
      estimatedCompletionTime={estimatedCompletionTime}
      status={status}
      trackedDownloadState={trackedDownloadState}
      trackedDownloadStatus={trackedDownloadStatus}
      statusMessages={statusMessages}
      errorMessage={errorMessage}
      progressBar={
        <CircularProgressBar
          progress={progress}
          size={20}
          strokeWidth={2}
          strokeColor="#7a43b6"
        />
      }
    />
  );
}

export default CalendarEventQueueDetails;
