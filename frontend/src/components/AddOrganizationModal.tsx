import { useState, Fragment, useEffect } from 'react';
import { toast } from 'sonner';
import { Dialog, Transition } from '@headlessui/react';
import {
  XMarkIcon,
  CheckCircleIcon,
  ChevronDownIcon,
  CalendarIcon,
  ClockIcon,
  GlobeAltIcon,
  FolderIcon,
  TagIcon,
} from '@heroicons/react/24/outline';
import { useQualityProfiles } from '../api/hooks';
import apiClient from '../api/client';

type MonitorOption =
  | 'all'
  | 'future'
  | 'missing'
  | 'existing'
  | 'recent'
  | 'none';

type CardMonitorOption = 'main' | 'prelims' | 'all' | 'none';

interface AddOrganizationModalProps {
  isOpen: boolean;
  onClose: () => void;
  organizationName: string;
  onSuccess: () => void;
}

export default function AddOrganizationModal({
  isOpen,
  onClose,
  organizationName,
  onSuccess,
}: AddOrganizationModalProps) {
  const [monitored, setMonitored] = useState(true);
  const [monitorOption, setMonitorOption] = useState<MonitorOption>('future');
  const [cardMonitorOption, setCardMonitorOption] = useState<CardMonitorOption>('all');
  const [qualityProfileId, setQualityProfileId] = useState<number | null>(null);
  const [rootFolder, setRootFolder] = useState('/data/media/fights');
  const [organizationFolder, setOrganizationFolder] = useState(true);
  const [tags, setTags] = useState<string>('');
  const [isImporting, setIsImporting] = useState(false);
  const [importProgress, setImportProgress] = useState<{
    imported: number;
    skipped: number;
    failed: number;
  } | null>(null);

  const { data: qualityProfiles } = useQualityProfiles();

  // Set default quality profile when profiles are loaded
  useEffect(() => {
    if (qualityProfiles && qualityProfiles.length > 0 && qualityProfileId === null) {
      const defaultProfile = qualityProfiles.find((p: any) => p.isDefault);
      if (defaultProfile) {
        setQualityProfileId(defaultProfile.id);
      }
    }
  }, [qualityProfiles, qualityProfileId]);

  const handleImport = async () => {
    // VALIDATION: Check if quality profile is selected
    if (qualityProfileId === null) {
      toast.error('Quality Profile Required', {
        description: 'Please select a quality profile before importing this organization.',
      });
      return;
    }

    // VALIDATION: Check if a default quality profile exists
    const hasDefaultProfile = qualityProfiles?.some((p: any) => p.isDefault);
    if (!hasDefaultProfile) {
      toast.error('Default Quality Profile Required', {
        description: 'Please set a default quality profile in Settings → Profiles before importing organizations.',
      });
      return;
    }

    setIsImporting(true);
    setImportProgress(null);
    try {
      const response = await apiClient.post('/organization/import', {
        organizationName,
        qualityProfileId,
        monitored,
        dateFilter: monitorOption === 'future' ? 'future' : 'all',
        cardMonitorOption,
        rootFolder,
        organizationFolder,
        tags: tags.split(',').map(t => t.trim()).filter(t => t),
      });

      if (response.data.success) {
        setImportProgress({
          imported: response.data.imported,
          skipped: response.data.skipped,
          failed: response.data.failed,
        });

        // Show success message
        toast.success('Organization Imported', {
          description: response.data.message,
          duration: 5000,
        });

        // Call onSuccess to refresh
        onSuccess();

        // Close modal after short delay
        setTimeout(() => {
          onClose();
        }, 1000);
      }
    } catch (error: any) {
      console.error('Failed to import organization:', error);
      const errorMessage =
        error.response?.data?.message || 'Failed to import organization. Please try again.';
      toast.error('Import Failed', {
        description: errorMessage,
      });
    } finally {
      setIsImporting(false);
    }
  };

  const getMonitorDescription = (option: MonitorOption) => {
    switch (option) {
      case 'all':
        return 'Monitor all events including past events';
      case 'future':
        return 'Monitor only upcoming events';
      case 'missing':
        return 'Monitor events that are missing from your library';
      case 'existing':
        return 'Monitor only events already in your library';
      case 'recent':
        return 'Monitor recent and upcoming events';
      case 'none':
        return 'Do not monitor any events';
      default:
        return '';
    }
  };

  const getCardMonitorDescription = (option: CardMonitorOption) => {
    switch (option) {
      case 'main':
        return 'Monitor only Main Cards for all events';
      case 'prelims':
        return 'Monitor only Prelims for all events';
      case 'all':
        return 'Monitor all fight cards (Main + Prelims)';
      case 'none':
        return 'Do not monitor any specific cards';
      default:
        return '';
    }
  };

  return (
    <Transition appear show={isOpen} as={Fragment}>
      <Dialog as="div" className="relative z-50" onClose={onClose}>
        <Transition.Child
          as={Fragment}
          enter="ease-out duration-300"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-200"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black/80" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-y-auto">
          <div className="flex min-h-full items-center justify-center p-4">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-3xl transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 shadow-2xl transition-all">
                {/* Header */}
                <div className="relative bg-gradient-to-r from-gray-900 via-red-950/20 to-gray-900 border-b border-red-900/30 p-6">
                  <div className="flex items-center justify-between">
                    <div>
                      <h2 className="text-2xl font-bold text-white mb-1">Add Organization</h2>
                      <p className="text-gray-400 text-sm flex items-center">
                        <GlobeAltIcon className="w-4 h-4 mr-1" />
                        {organizationName}
                      </p>
                    </div>
                    <button
                      onClick={onClose}
                      className="p-2 rounded-lg bg-black/50 hover:bg-black/70 transition-colors"
                      disabled={isImporting}
                    >
                      <XMarkIcon className="w-6 h-6 text-white" />
                    </button>
                  </div>
                </div>

                {/* Content */}
                <div className="p-6 space-y-6 max-h-[70vh] overflow-y-auto">
                  {/* Root Folder */}
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      <FolderIcon className="w-4 h-4 inline mr-2" />
                      Root Folder
                    </label>
                    <input
                      type="text"
                      value={rootFolder}
                      onChange={(e) => setRootFolder(e.target.value)}
                      disabled={isImporting}
                      className="w-full bg-gray-800 border border-red-900/20 text-white rounded-lg px-3 py-2 focus:ring-2 focus:ring-red-600 focus:border-transparent disabled:opacity-50"
                      placeholder="/data/media/fights"
                    />
                    <p className="text-gray-500 text-xs mt-1">
                      Path where fight events will be stored
                    </p>
                  </div>

                  {/* Organization Folder Toggle */}
                  <div>
                    <label className="flex items-center justify-between p-3 bg-gray-800/50 rounded-lg border border-red-900/20 cursor-pointer">
                      <div>
                        <div className="text-white font-medium mb-1">
                          <FolderIcon className="w-4 h-4 inline mr-2" />
                          Create Organization Folder
                        </div>
                        <div className="text-gray-400 text-sm">
                          Create subfolder: {rootFolder}/{organizationName}
                        </div>
                      </div>
                      <button
                        type="button"
                        onClick={() => setOrganizationFolder(!organizationFolder)}
                        disabled={isImporting}
                        className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                          organizationFolder ? 'bg-red-600' : 'bg-gray-600'
                        }`}
                      >
                        <span
                          className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                            organizationFolder ? 'translate-x-6' : 'translate-x-1'
                          }`}
                        />
                      </button>
                    </label>
                  </div>

                  {/* Monitor Toggle */}
                  <div>
                    <label className="flex items-center justify-between p-3 bg-gray-800/50 rounded-lg border border-red-900/20 cursor-pointer">
                      <div>
                        <div className="text-white font-medium mb-1">
                          <ClockIcon className="w-4 h-4 inline mr-2" />
                          Monitor Organization
                        </div>
                        <div className="text-gray-400 text-sm">
                          Automatically search for new events and downloads
                        </div>
                      </div>
                      <button
                        type="button"
                        onClick={() => setMonitored(!monitored)}
                        disabled={isImporting}
                        className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                          monitored ? 'bg-red-600' : 'bg-gray-600'
                        }`}
                      >
                        <span
                          className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                            monitored ? 'translate-x-6' : 'translate-x-1'
                          }`}
                        />
                      </button>
                    </label>
                  </div>

                  {/* Monitor Options */}
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      <CalendarIcon className="w-4 h-4 inline mr-2" />
                      Events to Import & Monitor
                    </label>
                    <div className="space-y-2">
                      {(['all', 'future', 'missing', 'existing', 'recent', 'none'] as MonitorOption[]).map((option) => (
                        <label
                          key={option}
                          className="flex items-center space-x-3 p-3 bg-gray-800/50 rounded-lg border border-red-900/20 hover:border-red-600/50 transition-colors cursor-pointer"
                        >
                          <input
                            type="radio"
                            name="monitorOption"
                            value={option}
                            checked={monitorOption === option}
                            onChange={(e) => setMonitorOption(e.target.value as MonitorOption)}
                            className="w-4 h-4 text-red-600 focus:ring-red-600"
                            disabled={isImporting}
                          />
                          <div className="flex-1">
                            <div className="text-white font-medium capitalize">{option} Events</div>
                            <div className="text-gray-400 text-sm">{getMonitorDescription(option)}</div>
                          </div>
                        </label>
                      ))}
                    </div>
                  </div>

                  {/* Card Monitor Options */}
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Fight Card Monitoring
                    </label>
                    <div className="space-y-2">
                      {(['all', 'main', 'prelims', 'none'] as CardMonitorOption[]).map((option) => (
                        <label
                          key={option}
                          className="flex items-center space-x-3 p-3 bg-gray-800/50 rounded-lg border border-red-900/20 hover:border-red-600/50 transition-colors cursor-pointer"
                        >
                          <input
                            type="radio"
                            name="cardMonitorOption"
                            value={option}
                            checked={cardMonitorOption === option}
                            onChange={(e) => setCardMonitorOption(e.target.value as CardMonitorOption)}
                            className="w-4 h-4 text-red-600 focus:ring-red-600"
                            disabled={isImporting}
                          />
                          <div className="flex-1">
                            <div className="text-white font-medium capitalize">
                              {option === 'all' ? 'All Cards' : option === 'main' ? 'Main Cards Only' : option === 'prelims' ? 'Prelims Only' : 'None'}
                            </div>
                            <div className="text-gray-400 text-sm">{getCardMonitorDescription(option)}</div>
                          </div>
                        </label>
                      ))}
                    </div>
                  </div>

                  {/* Quality Profile */}
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Quality Profile
                    </label>
                    <select
                      value={qualityProfileId ?? ''}
                      onChange={(e) =>
                        setQualityProfileId(e.target.value ? Number(e.target.value) : null)
                      }
                      disabled={isImporting}
                      className={`w-full bg-gray-800 border rounded-lg px-3 py-2 focus:ring-2 focus:ring-red-600 focus:border-transparent disabled:opacity-50 ${
                        qualityProfileId === null ? 'border-yellow-600 text-yellow-400' : 'border-red-900/20 text-white'
                      }`}
                    >
                      <option value="">Select a Quality Profile *</option>
                      {qualityProfiles?.map((profile: any) => (
                        <option key={profile.id} value={profile.id}>
                          {profile.name}{profile.isDefault ? ' (Default)' : ''}
                        </option>
                      ))}
                    </select>
                    <p className="text-gray-500 text-xs mt-1">
                      Quality profile to use for downloading events
                    </p>
                  </div>

                  {/* Tags */}
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      <TagIcon className="w-4 h-4 inline mr-2" />
                      Tags
                    </label>
                    <input
                      type="text"
                      value={tags}
                      onChange={(e) => setTags(e.target.value)}
                      disabled={isImporting}
                      className="w-full bg-gray-800 border border-red-900/20 text-white rounded-lg px-3 py-2 focus:ring-2 focus:ring-red-600 focus:border-transparent disabled:opacity-50"
                      placeholder="e.g., mma, ufc, premium (comma separated)"
                    />
                    <p className="text-gray-500 text-xs mt-1">
                      Tags for organization and filtering (comma separated)
                    </p>
                  </div>

                  {/* Import Progress */}
                  {importProgress && (
                    <div className="bg-green-900/20 border border-green-600/50 rounded-lg p-4">
                      <div className="flex items-start space-x-3">
                        <CheckCircleIcon className="w-6 h-6 text-green-400 flex-shrink-0 mt-0.5" />
                        <div className="flex-1">
                          <h4 className="text-white font-medium mb-2">Import Completed</h4>
                          <div className="space-y-1 text-sm">
                            <p className="text-green-400">✓ Imported: {importProgress.imported} events</p>
                            {importProgress.skipped > 0 && (
                              <p className="text-gray-400">⊘ Skipped: {importProgress.skipped} (already exist)</p>
                            )}
                            {importProgress.failed > 0 && (
                              <p className="text-red-400">✗ Failed: {importProgress.failed}</p>
                            )}
                          </div>
                        </div>
                      </div>
                    </div>
                  )}

                  {/* Info Box */}
                  <div className="bg-blue-900/20 border border-blue-600/50 rounded-lg p-4">
                    <div className="flex items-start space-x-3">
                      <GlobeAltIcon className="w-6 h-6 text-blue-400 flex-shrink-0 mt-0.5" />
                      <div className="flex-1">
                        <h4 className="text-white font-medium mb-1">About Import</h4>
                        <p className="text-gray-400 text-sm">
                          This will import all events for <span className="text-white font-medium">{organizationName}</span> from
                          the metadata API based on your monitoring settings. Events already in your library will be skipped.
                        </p>
                      </div>
                    </div>
                  </div>
                </div>

                {/* Footer */}
                <div className="px-6 py-4 bg-gray-900/50 border-t border-red-900/30 flex justify-between items-center">
                  <button
                    onClick={onClose}
                    disabled={isImporting}
                    className="px-4 py-2 text-gray-400 hover:text-white transition-colors disabled:opacity-50"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={handleImport}
                    disabled={isImporting}
                    className="px-6 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center space-x-2"
                  >
                    {isImporting ? (
                      <>
                        <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                        <span>Importing...</span>
                      </>
                    ) : (
                      <>
                        <CheckCircleIcon className="w-5 h-5" />
                        <span>Import Organization</span>
                      </>
                    )}
                  </button>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}
