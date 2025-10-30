import type { ReactNode } from 'react';

interface SettingsHeaderProps {
  title: string;
  subtitle?: string;
  onSave: () => void;
  isSaving?: boolean;
  hasUnsavedChanges?: boolean;
  saveButtonText?: string;
  children?: ReactNode;
}

export default function SettingsHeader({
  title,
  subtitle,
  onSave,
  isSaving = false,
  hasUnsavedChanges = false,
  saveButtonText = 'Save Settings',
  children,
}: SettingsHeaderProps) {
  return (
    <div className="sticky top-0 z-30 bg-gradient-to-r from-gray-900 via-black to-gray-900 border-b border-red-900/30 backdrop-blur-sm mb-8 -mt-4">
      <div className="flex items-center justify-between p-6">
        <div>
          <h1 className="text-3xl font-bold text-white mb-1">{title}</h1>
          {subtitle && <p className="text-gray-400">{subtitle}</p>}
        </div>
        <div className="flex items-center space-x-4">
          {children}
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
        </div>
      </div>
    </div>
  );
}
