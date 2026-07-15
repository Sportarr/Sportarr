import type { ReactNode } from 'react';

interface SettingsHeaderProps {
  title: string;
  subtitle?: string;
  onSave?: () => void;
  isSaving?: boolean;
  hasUnsavedChanges?: boolean;
  saveButtonText?: string;
  showSaveButton?: boolean;
  children?: ReactNode;
}

/**
 * Settings page header. On phones it is a normal block that scrolls away with
 * the page (nothing pinned eating screen space); when there are unsaved
 * changes a single floating Save pill appears above the tab bar so saving
 * never requires scrolling back up. On md+ the whole bar is sticky - desktop
 * has the room and keeps title, toggles, and Save always in reach.
 */
export default function SettingsHeader({
  title,
  subtitle,
  onSave,
  isSaving = false,
  hasUnsavedChanges = false,
  saveButtonText = 'Save Settings',
  showSaveButton = true,
  children,
}: SettingsHeaderProps) {
  return (
    <>
      <div className="md:sticky md:top-0 z-30 bg-gradient-to-r from-gray-900 via-black to-gray-900 border-b border-red-900/30 backdrop-blur-sm mb-8">
        <div className="flex flex-col gap-4 p-4 sm:p-6 md:flex-row md:items-start md:justify-between">
          <div className="min-w-0">
            <h1 className="text-3xl font-bold text-white mb-1">{title}</h1>
            {subtitle && <p className="text-gray-400">{subtitle}</p>}
          </div>
          <div className="flex flex-wrap items-center gap-3 md:justify-end">
            {children}
            {showSaveButton && onSave && (
              <div className="relative">
                <button
                  onClick={onSave}
                  disabled={isSaving}
                  className={`px-6 py-2 rounded-lg transition-all flex items-center space-x-2 ${
                    hasUnsavedChanges
                      ? 'bg-red-600 hover:bg-red-700 text-white shadow-lg shadow-red-600/50 animate-pulse'
                      : 'bg-red-600 hover:bg-red-700 text-white'
                  } disabled:opacity-50 disabled:cursor-not-allowed disabled:animate-none`}
                >
                  {isSaving ? (
                    <>
                      <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                      <span>Saving...</span>
                    </>
                  ) : (
                    <span>{saveButtonText}</span>
                  )}
                </button>
                {hasUnsavedChanges && !isSaving && (
                  <span className="absolute -top-1 -right-1 flex h-3 w-3">
                    <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-red-400 opacity-75"></span>
                    <span className="relative inline-flex rounded-full h-3 w-3 bg-red-500"></span>
                  </span>
                )}
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Phone-only floating Save: appears above the tab bar only while there
          is something to save, so the header itself never needs to pin. */}
      {showSaveButton && onSave && hasUnsavedChanges && (
        <div
          className="fixed inset-x-0 z-40 flex justify-end px-4 md:hidden pointer-events-none"
          style={{ bottom: 'calc(4.5rem + env(safe-area-inset-bottom))' }}
        >
          <button
            onClick={onSave}
            disabled={isSaving}
            className="pointer-events-auto flex items-center gap-2 rounded-full bg-red-600 px-5 py-3 text-sm font-semibold text-white shadow-xl shadow-red-900/50 transition-colors hover:bg-red-700 disabled:opacity-60"
          >
            {isSaving ? (
              <>
                <div className="h-4 w-4 animate-spin rounded-full border-b-2 border-white"></div>
                Saving...
              </>
            ) : (
              saveButtonText
            )}
          </button>
        </div>
      )}
    </>
  );
}
