import { Fragment, useEffect, useRef, useState } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { XMarkIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../api/client';
import FileMetadataEditor, { type FileMetadataEditorValues } from './FileMetadataEditor';

/**
 * Modal wrapper around <FileMetadataEditor> for the post-import edit flow.
 * Saves via PUT /api/event-files/{id} (single) or PUT /api/event-files/editor
 * (bulk; one set of values applied to N file ids).
 *
 * For pre-import flows the host should embed <FileMetadataEditor> directly
 * inside its own form and pass the values through to the import endpoint —
 * no modal, no API call here.
 */

export interface FileMetadataEditModalProps {
  isOpen: boolean;
  onClose: () => void;
  /** Single file id, or list of ids for bulk-edit. */
  fileIds: number[];
  /** Pre-fill the editor with current values. For bulk, pass the first file's
   *  values or empty so dropdowns appear blank. */
  initialValues: FileMetadataEditorValues;
  /** Whether to show PartName/PartNumber. Hide for non-multi-part events. */
  showPartFields?: boolean;
  /** Called after a successful save with the updated EventFile DTOs. */
  onSaved?: (updated: any[]) => void;
  /** Context for league-aware Part dropdowns + DB-known release-group list. */
  leagueId?: number;
  eventId?: number;
}

export default function FileMetadataEditModal({
  isOpen,
  onClose,
  fileIds,
  initialValues,
  showPartFields = true,
  onSaved,
  leagueId,
  eventId,
}: FileMetadataEditModalProps) {
  const [values, setValues] = useState<FileMetadataEditorValues>(initialValues);
  const [saving, setSaving] = useState(false);

  // Re-seed local state ONLY when the modal transitions from closed to open.
  // Watching `initialValues` here is a footgun: the parent recreates that
  // object on every render, so any unrelated parent re-render (ReactQuery
  // poll, sibling state change) would wipe the user's in-progress edits and
  // make backspaces feel like they "undo themselves". Tracking the open
  // transition with a ref makes the seeding deterministic.
  const wasOpen = useRef(false);
  useEffect(() => {
    if (isOpen && !wasOpen.current) {
      setValues(initialValues);
    }
    wasOpen.current = isOpen;
  }, [isOpen, initialValues]);

  const isBulk = fileIds.length > 1;

  const save = async () => {
    if (fileIds.length === 0) return;
    setSaving(true);
    try {
      const patch = stripUntouched(values, initialValues);
      // Log the diff so it's easy to verify in DevTools that the user-visible
      // edits actually translate into a non-empty request body. An empty
      // patch is the most common cause of "save toast appeared but nothing
      // changed" — the editor's local state never picked up the keystrokes.
      // eslint-disable-next-line no-console
      console.log('[FileMetadataEdit] saving', { fileIds, isBulk, patch, initialValues, values });

      if (Object.keys(patch).length === 0) {
        toast.warning('No changes to save');
        setSaving(false);
        return;
      }

      let response;
      if (isBulk) {
        response = await apiClient.put('/event-files/editor', {
          eventFileIds: fileIds,
          ...patch,
        });
        toast.success(`Updated ${fileIds.length} files`);
      } else {
        response = await apiClient.put(`/event-files/${fileIds[0]}`, patch);
        toast.success('File updated');
      }
      // eslint-disable-next-line no-console
      console.log('[FileMetadataEdit] server response', response.data);
      onSaved?.(Array.isArray(response.data) ? response.data : [response.data]);
      onClose();
    } catch (err: any) {
      const detail = err?.response?.data?.error
        ?? err?.response?.data?.detail
        ?? err?.message
        ?? 'Save failed';
      toast.error(detail);
      // eslint-disable-next-line no-console
      console.error('[FileMetadataEdit] save failed', err);
    } finally {
      setSaving(false);
    }
  };

  const hideFields: Array<keyof FileMetadataEditorValues> = showPartFields
    ? []
    : ['partName', 'partNumber'];

  return (
    <Transition appear show={isOpen} as={Fragment}>
      <Dialog as="div" className="relative z-50" onClose={onClose}>
        <Transition.Child
          as={Fragment}
          enter="ease-out duration-200"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-150"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black/70" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-hidden">
          <div className="flex min-h-full items-center justify-center p-4">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-200"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-150"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-2xl max-h-[calc(100vh-2rem)] overflow-y-auto rounded-lg bg-gray-900 border border-gray-700 shadow-2xl">
                <div className="flex items-center justify-between px-5 py-4 border-b border-gray-700">
                  <Dialog.Title className="text-lg font-semibold text-white">
                    {isBulk ? `Edit ${fileIds.length} Files` : 'Edit File Metadata'}
                  </Dialog.Title>
                  <button onClick={onClose} className="text-gray-400 hover:text-white">
                    <XMarkIcon className="w-5 h-5" />
                  </button>
                </div>

                <div className="px-5 py-5">
                  {isBulk && (
                    <p className="text-xs text-amber-400 mb-3">
                      Bulk edit: only fields you change will be applied to all {fileIds.length} files.
                      Empty fields below leave the existing per-file values alone.
                    </p>
                  )}
                  <FileMetadataEditor
                    value={values}
                    onChange={setValues}
                    hideFields={hideFields}
                    leagueId={leagueId}
                    eventId={eventId}
                  />
                </div>

                <div className="flex items-center justify-end gap-2 px-5 py-3 bg-gray-800/40 border-t border-gray-700 rounded-b-lg">
                  <button
                    onClick={onClose}
                    disabled={saving}
                    className="px-4 py-1.5 rounded text-sm text-gray-200 hover:bg-gray-800"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={save}
                    disabled={saving}
                    className="px-4 py-1.5 rounded text-sm bg-blue-700 hover:bg-blue-600 text-white disabled:opacity-50"
                  >
                    {saving ? 'Saving…' : 'Save'}
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

/**
 * Build the patch body that goes to the server: only include fields the user
 * actually modified. For bulk edits that means truly empty fields don't
 * inadvertently overwrite per-file values; for single-file edits it keeps
 * the request minimal.
 */
function stripUntouched(
  current: FileMetadataEditorValues,
  initial: FileMetadataEditorValues,
): Partial<FileMetadataEditorValues> {
  const out: Partial<FileMetadataEditorValues> = {};
  const keys: Array<keyof FileMetadataEditorValues> = [
    'quality', 'source', 'codec', 'releaseGroup', 'originalTitle',
    'languages', 'indexerFlags', 'partName', 'partNumber',
  ];
  for (const k of keys) {
    const a = (current as any)[k];
    const b = (initial as any)[k];
    if (k === 'languages') {
      if (!arraysEqual(a ?? [], b ?? [])) out[k] = a as any;
      continue;
    }
    if (a !== b) out[k] = a as any;
  }
  return out;
}

function arraysEqual(a: string[], b: string[]) {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
}
