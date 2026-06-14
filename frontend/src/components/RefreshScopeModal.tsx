import { Fragment } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { ArrowPathIcon, ClockIcon, ExclamationTriangleIcon } from '@heroicons/react/24/outline';

export type RefreshScope = 'current' | 'full';

interface RefreshScopeModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: (scope: RefreshScope) => void;
  leagueName?: string;
}

/**
 * Two-option modal shown when the user clicks the league refresh button.
 *
 * Check-for-changes is the fast common case ("anything new?") and runs
 * an immediate hub changes poll: the server names exactly which seasons
 * changed (historical included) and only those get re-synced. The same
 * check runs automatically every 15 minutes in the background, so this
 * is "don't wait for the next cycle" rather than a different mechanism.
 *
 * Refresh-all walks every historical season the Sportarr API knows
 * about, blindly. It's the recovery tool: restored database, install
 * offline past the change feed's retention window, or "I don't trust
 * my local data, rebuild it from server truth".
 *
 * We split this into a modal rather than running both paths from the
 * single button so users don't inadvertently fire the heavy walk on
 * every click.
 */
export default function RefreshScopeModal({
  isOpen,
  onClose,
  onConfirm,
  leagueName,
}: RefreshScopeModalProps) {
  return (
    <Transition appear show={isOpen} as={Fragment} unmount={true}>
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
          <div className="flex min-h-full items-center justify-center p-4 text-center">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-lg mx-4 transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 text-left align-middle shadow-xl transition-all">
                <div className="p-4 md:p-6">
                  <div className="flex items-start gap-3 md:gap-4 mb-4">
                    <div className="flex-shrink-0 w-10 h-10 md:w-12 md:h-12 rounded-full bg-red-600/20 flex items-center justify-center">
                      <ArrowPathIcon className="w-5 h-5 md:w-6 md:h-6 text-red-400" />
                    </div>
                    <div className="flex-1 min-w-0">
                      <Dialog.Title as="h3" className="text-base md:text-lg font-bold text-white mb-1">
                        Sync {leagueName ?? 'League'}
                      </Dialog.Title>
                      <p className="text-xs md:text-sm text-gray-400">
                        Pick how to sync this league with the Sportarr API.
                      </p>
                    </div>
                  </div>

                  <div className="flex flex-col gap-3">
                    <button
                      onClick={() => onConfirm('current')}
                      className="text-left rounded-lg border border-red-900/40 bg-black/30 hover:bg-red-600/10 hover:border-red-600/60 p-4 transition-colors"
                    >
                      <div className="flex items-start gap-3">
                        <ClockIcon className="w-5 h-5 text-red-400 flex-shrink-0 mt-0.5" />
                        <div className="flex-1 min-w-0">
                          <div className="text-sm md:text-base font-semibold text-white mb-0.5">
                            Quick Sync
                            <span className="ml-2 text-xs font-normal text-green-400">recommended</span>
                          </div>
                          <p className="text-xs text-gray-400">
                            Asks the server what changed and applies exactly that — any season, any league you monitor. This also runs automatically every 15 minutes; use this to skip the wait.
                          </p>
                        </div>
                      </div>
                    </button>

                    <button
                      onClick={() => onConfirm('full')}
                      className="text-left rounded-lg border border-yellow-900/40 bg-black/30 hover:bg-yellow-600/10 hover:border-yellow-600/60 p-4 transition-colors"
                    >
                      <div className="flex items-start gap-3">
                        <ExclamationTriangleIcon className="w-5 h-5 text-yellow-400 flex-shrink-0 mt-0.5" />
                        <div className="flex-1 min-w-0">
                          <div className="text-sm md:text-base font-semibold text-white mb-0.5">
                            Deep Sync
                          </div>
                          <p className="text-xs text-gray-400">
                            Re-syncs every season this league has on the Sportarr API, including decades of historical events. Can take a few minutes. Use this for recovery — a restored backup, an install that was offline for weeks, or data you suspect has drifted.
                          </p>
                        </div>
                      </div>
                    </button>
                  </div>
                </div>

                <div className="border-t border-red-900/30 p-3 md:p-4 bg-black/30 flex justify-end">
                  <button
                    onClick={onClose}
                    className="px-3 md:px-4 py-1.5 md:py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg text-sm md:text-base font-medium transition-colors"
                  >
                    Cancel
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
