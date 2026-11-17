/**
 * An event search task records whether it grabbed a release.
 * A search that grabbed nothing still completes, but it is not a success.
 */
export function grabbedNothing(task: { status?: string; result?: string | null }): boolean {
  if (task.status !== undefined && task.status !== 'Completed') return false;
  if (!task.result) return false;
  try {
    return (JSON.parse(task.result) as { grabbed?: unknown }).grabbed === false;
  } catch {
    return false;
  }
}
