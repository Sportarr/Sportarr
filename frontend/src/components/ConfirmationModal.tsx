import { Fragment, type ReactNode } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { ExclamationTriangleIcon } from '@heroicons/react/24/outline';

interface ConfirmationModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: () => void;
  title?: string;
  message?: ReactNode;
  confirmText?: string;
  confirmButtonClass?: string;
  isLoading?: boolean;
  confirmDisabled?: boolean;
  mobileFullScreen?: boolean;
}

export default function ConfirmationModal({
  isOpen,
  onClose,
  onConfirm,
  title = '',
  message = '',
  confirmText = 'Confirm',
  confirmButtonClass = 'bg-red-600 hover:bg-red-700',
  isLoading = false,
  confirmDisabled = false,
  mobileFullScreen = false,
}: ConfirmationModalProps) {
  // Always render Transition to ensure cleanup callback runs
  // Use isOpen AND title/message existence to control visibility
  const hasContent = !!title || !!message;

  return (
    <Transition
      appear
      show={isOpen && hasContent}
      as={Fragment}
      unmount={true}
      afterLeave={() => {
        // Force cleanup: remove any lingering inert attributes that might block navigation
        document.querySelectorAll('[inert]').forEach((el) => {
          el.removeAttribute('inert');
        });
      }}
    >
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

        <div className="fixed inset-0 overflow-hidden">
          <div className={`flex min-h-full text-center ${mobileFullScreen ? 'items-end justify-center p-0 sm:items-center sm:p-4' : 'items-center justify-center p-4'}`}>
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className={`w-full transform bg-gradient-to-br from-gray-900 to-black border border-red-900/30 text-left align-middle shadow-xl transition-all ${mobileFullScreen ? 'flex max-h-dvh flex-col overflow-hidden rounded-t-xl sm:mx-4 sm:max-h-[calc(100vh-2rem)] sm:max-w-md sm:rounded-lg' : 'max-w-md mx-4 max-h-[calc(100vh-2rem)] overflow-y-auto rounded-lg'}`}>
                <div className={`p-4 md:p-6 ${mobileFullScreen ? 'min-h-0 overflow-y-auto' : ''}`}>
                  <div className="flex items-start gap-3 md:gap-4">
                    <div className="flex-shrink-0 w-10 h-10 md:w-12 md:h-12 rounded-full bg-red-600/20 flex items-center justify-center">
                      <ExclamationTriangleIcon className="w-5 h-5 md:w-6 md:h-6 text-red-400" />
                    </div>
                    <div className="flex-1 min-w-0">
                      <Dialog.Title as="h3" className="text-base md:text-lg font-bold text-white mb-1 md:mb-2">
                        {title}
                      </Dialog.Title>
                      <div className="text-xs md:text-sm text-gray-400">
                        {message}
                      </div>
                    </div>
                  </div>
                </div>

                <div className={`border-t border-red-900/30 p-3 md:p-4 bg-black/30 flex gap-2 md:gap-3 justify-end ${mobileFullScreen ? 'shrink-0 flex-col sm:flex-row' : ''}`}>
                  <button
                    onClick={onClose}
                    disabled={isLoading}
                    className={`px-3 md:px-4 py-1.5 md:py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg text-sm md:text-base font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${mobileFullScreen ? 'w-full sm:w-auto' : ''}`}
                  >
                    Cancel
                  </button>
                  <button
                    onClick={onConfirm}
                    disabled={isLoading || confirmDisabled}
                    className={`px-3 md:px-4 py-1.5 md:py-2 text-white rounded-lg text-sm md:text-base font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${mobileFullScreen ? 'w-full sm:w-auto' : ''} ${confirmButtonClass}`}
                  >
                    {isLoading ? 'Processing...' : confirmText}
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
