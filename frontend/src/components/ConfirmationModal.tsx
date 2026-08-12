import { Fragment } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { ExclamationTriangleIcon } from '@heroicons/react/24/outline';

interface ConfirmationModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: () => void;
  title?: string;
  message?: string;
  confirmText?: string;
  confirmButtonClass?: string;
  isLoading?: boolean;
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
              <Dialog.Panel className="w-full max-w-md mx-4 transform max-h-[calc(100vh-2rem)] overflow-y-auto rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 text-left align-middle shadow-xl transition-all">
                <div className="p-4 md:p-6">
                  <div className="flex items-start gap-3 md:gap-4">
                    <div className="flex-shrink-0 w-10 h-10 md:w-12 md:h-12 rounded-full bg-red-600/20 flex items-center justify-center">
                      <ExclamationTriangleIcon className="w-5 h-5 md:w-6 md:h-6 text-red-400" />
                    </div>
                    <div className="flex-1 min-w-0">
                      <Dialog.Title as="h3" className="text-base md:text-lg font-bold text-white mb-1 md:mb-2">
                        {title}
                      </Dialog.Title>
                      <p className="text-xs md:text-sm text-gray-400">
                        {message}
                      </p>
                    </div>
                  </div>
                </div>

                <div className="border-t border-red-900/30 p-3 md:p-4 bg-black/30 flex gap-2 md:gap-3 justify-end">
                  <button
                    onClick={onClose}
                    disabled={isLoading}
                    className="px-3 md:px-4 py-1.5 md:py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg text-sm md:text-base font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={onConfirm}
                    disabled={isLoading}
                    className={`px-3 md:px-4 py-1.5 md:py-2 text-white rounded-lg text-sm md:text-base font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${confirmButtonClass}`}
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
