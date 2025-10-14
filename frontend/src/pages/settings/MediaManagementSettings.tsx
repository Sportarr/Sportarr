import { useState } from 'react';
import { PlusIcon, FolderIcon, CheckIcon, XMarkIcon } from '@heroicons/react/24/outline';

interface MediaManagementSettingsProps {
  showAdvanced: boolean;
}

interface RootFolder {
  id: number;
  path: string;
  accessible: boolean;
  freeSpace: number;
}

export default function MediaManagementSettings({ showAdvanced }: MediaManagementSettingsProps) {
  const [rootFolders, setRootFolders] = useState<RootFolder[]>([
    { id: 1, path: '/data/fightarr', accessible: true, freeSpace: 500000000000 },
  ]);

  // File Management
  const [renameEvents, setRenameEvents] = useState(false);
  const [replaceIllegalCharacters, setReplaceIllegalCharacters] = useState(true);

  // Standard Event Format
  const [standardEventFormat, setStandardEventFormat] = useState(
    '{Event Title} - {Event Date} - {Organization}'
  );

  // Folders
  const [createEventFolders, setCreateEventFolders] = useState(true);
  const [deleteEmptyFolders, setDeleteEmptyFolders] = useState(false);

  // Importing
  const [skipFreeSpaceCheck, setSkipFreeSpaceCheck] = useState(false);
  const [minimumFreeSpace, setMinimumFreeSpace] = useState(100);
  const [useHardlinks, setUseHardlinks] = useState(true);
  const [importExtraFiles, setImportExtraFiles] = useState(false);
  const [extraFileExtensions, setExtraFileExtensions] = useState('srt,nfo');

  // File Management Advanced
  const [changeFileDate, setChangeFileDate] = useState('None');
  const [recycleBin, setRecycleBin] = useState('');
  const [recycleBinCleanup, setRecycleBinCleanup] = useState(7);
  const [setPermissions, setSetPermissions] = useState(false);
  const [chmodFolder, setChmodFolder] = useState('755');
  const [chownGroup, setChownGroup] = useState('');

  const formatBytes = (bytes: number) => {
    const gb = bytes / (1024 * 1024 * 1024);
    return `${gb.toFixed(2)} GB`;
  };

  return (
    <div className="max-w-4xl">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Media Management</h2>
        <p className="text-gray-400">Settings for file naming, root folders, and file management</p>
      </div>

      {/* Root Folders */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-xl font-semibold text-white">Root Folders</h3>
          <button className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors">
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Root Folder
          </button>
        </div>
        <p className="text-sm text-gray-400 mb-4">
          Root folders where Fightarr will store combat sports events
        </p>

        <div className="space-y-2">
          {rootFolders.map((folder) => (
            <div
              key={folder.id}
              className="flex items-center justify-between p-4 bg-black/30 rounded-lg border border-gray-800"
            >
              <div className="flex items-center flex-1">
                <FolderIcon className="w-5 h-5 text-red-400 mr-3" />
                <div className="flex-1">
                  <p className="text-white font-medium">{folder.path}</p>
                  <p className="text-sm text-gray-400">
                    Free Space: {formatBytes(folder.freeSpace)}
                  </p>
                </div>
              </div>
              <div className="flex items-center space-x-3">
                {folder.accessible ? (
                  <CheckIcon className="w-5 h-5 text-green-500" />
                ) : (
                  <XMarkIcon className="w-5 h-5 text-red-500" />
                )}
                <button className="text-gray-400 hover:text-white text-sm">Delete</button>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Event Naming */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <h3 className="text-xl font-semibold text-white mb-4">Event Naming</h3>

        <div className="space-y-4">
          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={renameEvents}
              onChange={(e) => setRenameEvents(e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Rename Events</span>
              <p className="text-sm text-gray-400 mt-1">
                Rename event files based on naming scheme
              </p>
            </div>
          </label>

          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={replaceIllegalCharacters}
              onChange={(e) => setReplaceIllegalCharacters(e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Replace Illegal Characters</span>
              <p className="text-sm text-gray-400 mt-1">
                Replace illegal characters with replacement character
              </p>
            </div>
          </label>

          {renameEvents && (
            <>
              <div>
                <label className="block text-white font-medium mb-2">Standard Event Format</label>
                <input
                  type="text"
                  value={standardEventFormat}
                  onChange={(e) => setStandardEventFormat(e.target.value)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                />
                <p className="text-sm text-gray-400 mt-2">
                  Available tokens: {'{Event Title}'}, {'{Organization}'}, {'{Event Date}'}, {'{Quality Full}'}, {'{Quality Title}'}
                </p>
              </div>

              <div className="p-4 bg-blue-950/30 border border-blue-900/50 rounded-lg">
                <p className="text-sm text-blue-300">
                  <strong>Example:</strong> UFC 300 - 2024-04-13 - Ultimate Fighting Championship.mkv
                </p>
              </div>
            </>
          )}
        </div>
      </div>

      {/* Folders */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <h3 className="text-xl font-semibold text-white mb-4">Folders</h3>

        <div className="space-y-4">
          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={createEventFolders}
              onChange={(e) => setCreateEventFolders(e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Create Event Folders</span>
              <p className="text-sm text-gray-400 mt-1">
                Create individual folders for each event
              </p>
            </div>
          </label>

          {showAdvanced && (
            <label className="flex items-start space-x-3 cursor-pointer">
              <input
                type="checkbox"
                checked={deleteEmptyFolders}
                onChange={(e) => setDeleteEmptyFolders(e.target.checked)}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-white font-medium">Delete Empty Folders</span>
                <p className="text-sm text-gray-400 mt-1">
                  Delete empty event folders during disk scan
                </p>
                <span className="inline-block mt-1 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                  Advanced
                </span>
              </div>
            </label>
          )}
        </div>
      </div>

      {/* Importing */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <h3 className="text-xl font-semibold text-white mb-4">Importing</h3>

        <div className="space-y-4">
          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={useHardlinks}
              onChange={(e) => setUseHardlinks(e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Use Hardlinks instead of Copy</span>
              <p className="text-sm text-gray-400 mt-1">
                Use hardlinks when copying files from torrents (requires same filesystem)
              </p>
            </div>
          </label>

          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={importExtraFiles}
              onChange={(e) => setImportExtraFiles(e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Import Extra Files</span>
              <p className="text-sm text-gray-400 mt-1">
                Import matching extra files (subtitles, nfo, etc)
              </p>
            </div>
          </label>

          {importExtraFiles && (
            <div>
              <label className="block text-white font-medium mb-2">Extra File Extensions</label>
              <input
                type="text"
                value={extraFileExtensions}
                onChange={(e) => setExtraFileExtensions(e.target.value)}
                placeholder="srt,nfo,jpg,png"
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              />
              <p className="text-sm text-gray-400 mt-1">
                Comma separated list of extra file extensions to import
              </p>
            </div>
          )}

          {showAdvanced && (
            <>
              <label className="flex items-start space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={skipFreeSpaceCheck}
                  onChange={(e) => setSkipFreeSpaceCheck(e.target.checked)}
                  className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <div>
                  <span className="text-white font-medium">Skip Free Space Check</span>
                  <p className="text-sm text-gray-400 mt-1">
                    Skip checking free space before importing
                  </p>
                  <span className="inline-block mt-1 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                    Advanced
                  </span>
                </div>
              </label>

              {!skipFreeSpaceCheck && (
                <div>
                  <label className="block text-white font-medium mb-2">Minimum Free Space</label>
                  <div className="flex items-center space-x-2">
                    <input
                      type="number"
                      value={minimumFreeSpace}
                      onChange={(e) => setMinimumFreeSpace(Number(e.target.value))}
                      className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <span className="text-gray-400">MB</span>
                  </div>
                  <p className="text-sm text-gray-400 mt-1">
                    Prevent import if it would leave less than this amount of free space
                  </p>
                  <span className="inline-block mt-1 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                    Advanced
                  </span>
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {/* File Management (Advanced) */}
      {showAdvanced && (
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <h3 className="text-xl font-semibold text-white mb-4">
            File Management
            <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
              Advanced
            </span>
          </h3>

          <div className="space-y-4">
            <div>
              <label className="block text-white font-medium mb-2">Recycle Bin Path</label>
              <input
                type="text"
                value={recycleBin}
                onChange={(e) => setRecycleBin(e.target.value)}
                placeholder="/path/to/recycle/bin"
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              />
              <p className="text-sm text-gray-400 mt-1">
                Files will be moved here instead of being deleted
              </p>
            </div>

            {recycleBin && (
              <div>
                <label className="block text-white font-medium mb-2">Recycle Bin Cleanup</label>
                <div className="flex items-center space-x-2">
                  <input
                    type="number"
                    value={recycleBinCleanup}
                    onChange={(e) => setRecycleBinCleanup(Number(e.target.value))}
                    className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  />
                  <span className="text-gray-400">days</span>
                </div>
                <p className="text-sm text-gray-400 mt-1">
                  Set to 0 to disable automatic cleanup
                </p>
              </div>
            )}

            <label className="flex items-start space-x-3 cursor-pointer">
              <input
                type="checkbox"
                checked={setPermissions}
                onChange={(e) => setSetPermissions(e.target.checked)}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-white font-medium">Set Permissions</span>
                <p className="text-sm text-gray-400 mt-1">
                  Set file permissions during import/rename (Linux/macOS only)
                </p>
              </div>
            </label>

            {setPermissions && (
              <>
                <div>
                  <label className="block text-white font-medium mb-2">chmod Folder</label>
                  <input
                    type="text"
                    value={chmodFolder}
                    onChange={(e) => setChmodFolder(e.target.value)}
                    placeholder="755"
                    className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  />
                </div>

                <div>
                  <label className="block text-white font-medium mb-2">chown Group</label>
                  <input
                    type="text"
                    value={chownGroup}
                    onChange={(e) => setChownGroup(e.target.value)}
                    placeholder="media"
                    className="w-64 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  />
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {/* Save Button */}
      <div className="flex justify-end">
        <button className="px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105">
          Save Changes
        </button>
      </div>
    </div>
  );
}
