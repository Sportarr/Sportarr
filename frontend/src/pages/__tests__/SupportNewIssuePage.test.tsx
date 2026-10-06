import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useEffect } from 'react';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import SupportNewIssuePage from '../SupportNewIssuePage';
import { clearSupportToken, SupportApiError } from '../../api/supportConnection';

const mocks = vi.hoisted(() => ({ request: vi.fn() }));

vi.mock('../../api/supportConnection', () => ({
  supportRequest: mocks.request,
  clearSupportToken: vi.fn(),
  SupportApiError: class extends Error {
    constructor(message: string, public status: number, public retryAfterSeconds: number | null = null) { super(message); }
  },
}));

vi.mock('../../components/SupportConnection', () => ({
  default: ({ onIdentityChange }: { onIdentityChange: (identity: unknown) => void }) => {
    useEffect(() => onIdentityChange({ session_id: 'synthetic', connected_providers: ['github'],
      github_posting: true, github_login: 'synthetic-user', expires_at: '2026-12-01T00:00:00Z' }), [onIdentityChange]);
    return <div>Account connected</div>;
  },
}));

function renderForm() {
  render(<MemoryRouter initialEntries={['/support/new']}><Routes>
    <Route path="/support/new" element={<SupportNewIssuePage />} />
    <Route path="/support/issues/:number" element={<div>Issue opened</div>} />
  </Routes></MemoryRouter>);
  fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Import does not complete' } });
  fireEvent.change(screen.getByLabelText('What happened'), {
    target: { value: 'The import remains pending after the download finishes.' },
  });
  fireEvent.change(screen.getByLabelText('Sportarr version and install type'), {
    target: { value: '4.1.8 Docker' },
  });
}

describe('SupportNewIssuePage', () => {
  beforeEach(() => {
    localStorage.clear();
    mocks.request.mockReset();
    vi.mocked(clearSupportToken).mockClear();
  });

  it('keeps the same issue request after a network error', async () => {
    mocks.request.mockRejectedValue(new Error('Network unavailable'));
    renderForm();
    await waitFor(() => expect(localStorage.getItem('sportarr-support-issue-draft')).toContain('Import does not complete'));
    const before = JSON.parse(localStorage.getItem('sportarr-support-issue-draft') || '{}').idempotencyKey;

    fireEvent.click(screen.getByRole('button', { name: 'Submit issue' }));

    expect(await screen.findByText('Network unavailable')).toBeInTheDocument();
    expect(JSON.parse(localStorage.getItem('sportarr-support-issue-draft') || '{}').idempotencyKey).toBe(before);
    expect(mocks.request).toHaveBeenCalledWith('/issues', expect.objectContaining({ method: 'POST' }));
  });

  it('replaces the request key only after a confirmed failed publication', async () => {
    mocks.request.mockResolvedValue({ report_id: 'synthetic-report', number: null,
      status: 'failed', error: 'GitHub rejected this issue.' });
    renderForm();
    await waitFor(() => expect(localStorage.getItem('sportarr-support-issue-draft')).toContain('Import does not complete'));
    const before = JSON.parse(localStorage.getItem('sportarr-support-issue-draft') || '{}').idempotencyKey;

    fireEvent.click(screen.getByRole('button', { name: 'Submit issue' }));

    expect(await screen.findByText('GitHub rejected this issue.')).toBeInTheDocument();
    await waitFor(() => expect(JSON.parse(localStorage.getItem('sportarr-support-issue-draft') || '{}').idempotencyKey).not.toBe(before));
  });

  it('keeps a pending report and its draft when the user leaves and returns', async () => {
    mocks.request.mockResolvedValueOnce({ report_id: 'first-report', number: null, status: 'pending' });
    render(<MemoryRouter initialEntries={['/support/new']}><Routes>
      <Route path="/support/new" element={<SupportNewIssuePage />} />
      <Route path="/support" element={<Link to="/support/new">New issue</Link>} />
      <Route path="/support/issues/:number" element={<div>Issue opened</div>} />
    </Routes></MemoryRouter>);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'First import issue' } });
    fireEvent.change(screen.getByLabelText('What happened'), { target: { value: 'The first import remains pending after download.' } });
    fireEvent.change(screen.getByLabelText('Sportarr version and install type'), { target: { value: '4.1.8 Docker' } });
    fireEvent.click(screen.getByRole('button', { name: 'Submit issue' }));

    expect(await screen.findByText(/Your issue is saved/)).toBeInTheDocument();
    expect(localStorage.getItem('sportarr-support-issue-draft')).toContain('First import issue');
    expect(localStorage.getItem('sportarr-support-pending-report')).toBe('first-report');
    fireEvent.click(screen.getByRole('link', { name: 'Back to issues' }));
    fireEvent.click(screen.getByRole('link', { name: 'New issue' }));

    expect(screen.getByLabelText('Title')).toHaveValue('First import issue');
    expect(screen.getByRole('button', { name: 'Issue saved' })).toBeDisabled();
    expect(mocks.request).toHaveBeenCalledTimes(1);
  });

  it('offers a bot fallback when GitHub posting permission expires', async () => {
    mocks.request.mockRejectedValue(new SupportApiError('Reconnect GitHub posting or choose the Sportarr bot', 409));
    renderForm();

    fireEvent.click(screen.getByRole('button', { name: 'Submit issue' }));

    expect(await screen.findByRole('dialog', { name: 'Choose issue author' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Submit through Sportarr' })).toBeInTheDocument();
  });

  it.each([
    'Issue request has already been used',
    'Issue details changed after submission. Start a new issue request',
  ])('keeps a changed issue editable after a request-key conflict: %s', async (message) => {
    mocks.request.mockRejectedValue(new SupportApiError(message, 409));
    renderForm();
    await waitFor(() => expect(localStorage.getItem('sportarr-support-issue-draft')).toContain('Import does not complete'));
    const before = JSON.parse(localStorage.getItem('sportarr-support-issue-draft') || '{}').idempotencyKey;

    fireEvent.click(screen.getByRole('button', { name: 'Submit issue' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(screen.queryByRole('dialog', { name: 'Choose issue author' })).not.toBeInTheDocument();
    await waitFor(() => expect(JSON.parse(localStorage.getItem('sportarr-support-issue-draft') || '{}').idempotencyKey).not.toBe(before));
    expect(screen.getByLabelText('Title')).toHaveValue('Import does not complete');
  });

  it('keeps the draft when the account session expires', async () => {
    mocks.request.mockRejectedValue(new SupportApiError('Reconnect Support', 401));
    renderForm();

    fireEvent.click(screen.getByRole('button', { name: 'Submit issue' }));

    await waitFor(() => expect(clearSupportToken).toHaveBeenCalledOnce());
    expect(localStorage.getItem('sportarr-support-issue-draft')).toContain('Import does not complete');
  });

  it('keeps the account connection when the server rejects a business rule', async () => {
    mocks.request.mockRejectedValue(new SupportApiError('Join the Discord server before creating an issue', 403));
    renderForm();

    fireEvent.click(screen.getByRole('button', { name: 'Submit issue' }));

    expect(await screen.findByText('Join the Discord server before creating an issue')).toBeInTheDocument();
    expect(clearSupportToken).not.toHaveBeenCalled();
  });

  it('shows remaining cooldown only after a blocked submit and keeps the draft', async () => {
    mocks.request.mockRejectedValue(new SupportApiError('Please wait before creating another issue', 429, 558));
    renderForm();
    const file = new File(['Synthetic log'], 'sportarr.log', { type: 'text/plain' });
    fireEvent.change(screen.getByLabelText('Choose files'), { target: { files: [file] } });
    expect(screen.queryByText(/9 minutes 18 seconds/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Submit issue' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Please wait 9 minutes 18 seconds before creating another issue.');
    expect(screen.getByLabelText('What happened')).toHaveValue('The import remains pending after the download finishes.');
    expect(localStorage.getItem('sportarr-support-issue-draft')).toContain('Import does not complete');
    expect(screen.getByText('sportarr.log')).toBeInTheDocument();
  });

  it('shows the server error without inventing a wait when no retry header exists', async () => {
    mocks.request.mockRejectedValue(new SupportApiError('Too many requests', 429));
    renderForm();
    fireEvent.click(screen.getByRole('button', { name: 'Submit issue' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Too many requests');
    expect(screen.getByRole('alert')).not.toHaveTextContent('minute');
  });

  it('adds a diagnostic file handed over from Logs', async () => {
    const file = new File(['SPORTARR SUPPORT BUNDLE v1\n[ERR] Synthetic failure'],
      'sportarr-support-01.txt', { type: 'text/plain' });
    Object.defineProperty(file, 'text', { value: () => Promise.resolve('SPORTARR SUPPORT BUNDLE v1\n[ERR] Synthetic failure') });

    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: { diagnosticFiles: [file] } }]}>
      <Routes><Route path="/support/new" element={<SupportNewIssuePage />} /></Routes>
    </MemoryRouter>);

    expect(await screen.findByText('sportarr-support-01.txt')).toBeInTheDocument();
    expect(await screen.findByText(/Synthetic failure/)).toBeInTheDocument();
  });

  it('prefills version from System Status without replacing a saved answer', async () => {
    localStorage.setItem('sportarr-support-issue-draft', JSON.stringify({
      category: 'bug', title: 'Saved issue', primary: 'A saved explanation remains in place.',
      secondary: '', tertiary: 'My manually entered version', idempotencyKey: 'b'.repeat(32), fileNames: [],
    }));
    const file = new File(['Version: 4.1.8'], 'sportarr-system-info.txt', { type: 'text/plain' });
    Object.defineProperty(file, 'text', { value: () => Promise.resolve('Version: 4.1.8') });
    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: {
      diagnosticFiles: [file], versionAndInstallType: '4.1.8 / Docker',
    } }]}><Routes><Route path="/support/new" element={<SupportNewIssuePage />} /></Routes></MemoryRouter>);

    expect(await screen.findByText('sportarr-system-info.txt')).toBeInTheDocument();
    expect(screen.getByLabelText('Sportarr version and install type')).toHaveValue('My manually entered version');
    expect(screen.getByLabelText('Title')).toHaveValue('Saved issue');
    expect(screen.getByRole('status')).toHaveTextContent('System info added to your existing draft');
    expect(mocks.request).not.toHaveBeenCalledWith('/issues', expect.anything());
  });

  it('prefills a new bug and clears only its automatic version on switch to feature', async () => {
    const file = new File(['Version: 4.1.8'], 'sportarr-system-info.txt', { type: 'text/plain' });
    Object.defineProperty(file, 'text', { value: () => Promise.resolve('Version: 4.1.8') });
    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: {
      diagnosticFiles: [file], versionAndInstallType: '4.1.8 / Docker',
    } }]}><Routes><Route path="/support/new" element={<SupportNewIssuePage />} /></Routes></MemoryRouter>);

    await waitFor(() => expect(screen.getByLabelText('Sportarr version and install type')).toHaveValue('4.1.8 / Docker'));
    fireEvent.click(screen.getByRole('button', { name: /Feature request/ }));
    expect(screen.getByLabelText('Example or alternative')).toHaveValue('');
    expect(screen.getByText('sportarr-system-info.txt')).toBeInTheDocument();
  });

  it('does not duplicate a system-info file when the handoff is repeated', async () => {
    let stamp = 1000;
    const makeFile = () => {
      const file = new File(['Version: 4.1.8'], 'sportarr-system-info.txt', { type: 'text/plain', lastModified: stamp++ });
      Object.defineProperty(file, 'text', { value: () => Promise.resolve('Version: 4.1.8') });
      return file;
    };
    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: {
      diagnosticFiles: [makeFile()], versionAndInstallType: '4.1.8 / Docker',
    } }]}><Routes>
      <Route path="/support/new" element={<><Link to="/system/status">System Status</Link><SupportNewIssuePage /></>} />
      <Route path="/system/status" element={<Link to="/support/new" state={{
        diagnosticFiles: [makeFile()], versionAndInstallType: '4.1.8 / Docker',
      }}>Create support issue</Link>} />
    </Routes></MemoryRouter>);
    expect(await screen.findByText('sportarr-system-info.txt')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('link', { name: 'System Status' }));
    fireEvent.click(screen.getByRole('link', { name: 'Create support issue' }));
    await waitFor(() => expect(screen.getAllByText('sportarr-system-info.txt')).toHaveLength(1));
    expect(screen.getByRole('status')).toHaveTextContent('System info is already attached');
  });

  it('keeps automatic version out of Feature after leaving and returning', async () => {
    const file = new File(['Version: 4.1.8'], 'sportarr-system-info.txt', { type: 'text/plain' });
    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: {
      diagnosticFiles: [file], versionAndInstallType: '4.1.8 / Docker',
    } }]}><Routes>
      <Route path="/support/new" element={<><Link to="/status">Status</Link><SupportNewIssuePage /></>} />
      <Route path="/status" element={<Link to="/support/new">Return to issue</Link>} />
    </Routes></MemoryRouter>);
    await waitFor(() => expect(screen.getByLabelText('Sportarr version and install type')).toHaveValue('4.1.8 / Docker'));
    fireEvent.click(screen.getByRole('link', { name: 'Status' }));
    fireEvent.click(screen.getByRole('link', { name: 'Return to issue' }));
    fireEvent.click(screen.getByRole('button', { name: /Feature request/ }));
    expect(screen.getByLabelText('Example or alternative')).toHaveValue('');
  });

  it('replaces a same-named support log with the newer handoff', async () => {
    const makeLog = (content: string, lastModified: number) => {
      const file = new File([content], 'sportarr-support-01.txt', { type: 'text/plain', lastModified });
      Object.defineProperty(file, 'text', { value: () => Promise.resolve(content) });
      return file;
    };
    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: {
      diagnosticFiles: [makeLog('Old synthetic log', 1000)],
    } }]}><Routes>
      <Route path="/support/new" element={<><Link to="/logs">Logs</Link><SupportNewIssuePage /></>} />
      <Route path="/logs" element={<Link to="/support/new" state={{
        diagnosticFiles: [makeLog('New synthetic log', 2000)],
      }}>Create issue</Link>} />
    </Routes></MemoryRouter>);
    expect(await screen.findByText(/Old synthetic log/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('link', { name: 'Logs' }));
    fireEvent.click(screen.getByRole('link', { name: 'Create issue' }));
    expect(await screen.findByText(/New synthetic log/)).toBeInTheDocument();
    expect(screen.getAllByText('sportarr-support-01.txt')).toHaveLength(1);
    expect(screen.queryByText(/Old synthetic log/)).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Diagnostic log added to your existing draft');
  });

  it('removes old extra log parts when a newer bundle has fewer parts', async () => {
    const makeLog = (name: string, content: string) => {
      const file = new File([content], name, { type: 'text/plain' });
      Object.defineProperty(file, 'text', { value: () => Promise.resolve(content) });
      return file;
    };
    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: {
      diagnosticFiles: [makeLog('sportarr-support-01.txt', 'Old part one'),
        makeLog('sportarr-support-02.txt', 'Old part two'), makeLog('sportarr-support-03.txt', 'Old part three')],
    } }]}><Routes>
      <Route path="/support/new" element={<><Link to="/logs">Logs</Link><SupportNewIssuePage /></>} />
      <Route path="/logs" element={<Link to="/support/new" state={{ diagnosticFiles: [
        makeLog('sportarr-support-01.txt', 'New part one'), makeLog('sportarr-support-02.txt', 'New part two'),
      ] }}>Create issue</Link>} />
    </Routes></MemoryRouter>);
    expect(await screen.findByText('sportarr-support-03.txt')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('link', { name: 'Logs' }));
    fireEvent.click(screen.getByRole('link', { name: 'Create issue' }));
    expect(await screen.findByText(/New part one/)).toBeInTheDocument();
    expect(screen.queryByText('sportarr-support-03.txt')).not.toBeInTheDocument();
    expect(localStorage.getItem('sportarr-support-issue-draft')).not.toContain('sportarr-support-03.txt');
  });

  it('keeps the earlier log when a replacement bundle exceeds the file limit', async () => {
    const oldLog = new File(['Earlier synthetic log'], 'sportarr-support-01.txt', { type: 'text/plain' });
    Object.defineProperty(oldLog, 'text', { value: () => Promise.resolve('Earlier synthetic log') });
    const oversized = new File([new Uint8Array(10 * 1024 * 1024 + 1)],
      'sportarr-support-01.txt', { type: 'text/plain' });
    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: {
      diagnosticFiles: [oldLog],
    } }]}><Routes>
      <Route path="/support/new" element={<><Link to="/logs">Logs</Link><SupportNewIssuePage /></>} />
      <Route path="/logs" element={<Link to="/support/new" state={{ diagnosticFiles: [oversized] }}>Create issue</Link>} />
    </Routes></MemoryRouter>);
    expect(await screen.findByText(/Earlier synthetic log/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('link', { name: 'Logs' }));
    fireEvent.click(screen.getByRole('link', { name: 'Create issue' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Your earlier files are still attached');
    expect(screen.getByText(/Earlier synthetic log/)).toBeInTheDocument();
    expect(screen.getAllByText('sportarr-support-01.txt')).toHaveLength(1);
    expect(localStorage.getItem('sportarr-support-issue-draft')).toContain('sportarr-support-01.txt');
  });

  it('clears a removed diagnostic preview while keeping another support file', async () => {
    const makeLog = (name: string, content: string) => {
      const file = new File([content], name, { type: 'text/plain' });
      Object.defineProperty(file, 'text', { value: () => Promise.resolve(content) });
      return file;
    };
    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: {
      diagnosticFiles: [makeLog('sportarr-support-01.txt', 'First synthetic log'),
        makeLog('sportarr-support-02.txt', 'Second synthetic log')],
    } }]}><Routes><Route path="/support/new" element={<SupportNewIssuePage />} /></Routes></MemoryRouter>);
    expect(await screen.findByText(/First synthetic log/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Remove sportarr-support-01.txt' }));
    expect(await screen.findByText(/Second synthetic log/)).toBeInTheDocument();
    expect(screen.queryByText(/First synthetic log/)).not.toBeInTheDocument();
  });

  it('does not replace the diagnostic log preview with system info', async () => {
    const log = new File(['SPORTARR SUPPORT BUNDLE v1\n[ERR] Log failure'], 'sportarr-support-01.txt', { type: 'text/plain' });
    Object.defineProperty(log, 'text', { value: () => Promise.resolve('SPORTARR SUPPORT BUNDLE v1\n[ERR] Log failure') });
    const info = new File(['Version: 4.1.8'], 'sportarr-system-info.txt', { type: 'text/plain' });
    Object.defineProperty(info, 'text', { value: () => Promise.resolve('Version: 4.1.8') });
    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: { diagnosticFiles: [log] } }] }><Routes>
      <Route path="/support/new" element={<><Link to="/status">System Status</Link><SupportNewIssuePage /></>} />
      <Route path="/status" element={<Link to="/support/new" state={{ diagnosticFiles: [info] }}>Create support issue</Link>} />
    </Routes></MemoryRouter>);
    expect(await screen.findByText(/Log failure/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('link', { name: 'System Status' }));
    fireEvent.click(screen.getByRole('link', { name: 'Create support issue' }));
    expect(await screen.findByText(/Log failure/)).toBeInTheDocument();
    expect(screen.queryByText('Version: 4.1.8')).not.toBeInTheDocument();
  });

  it('does not put system version in a feature example', async () => {
    localStorage.setItem('sportarr-support-issue-draft', JSON.stringify({
      category: 'feature', title: '', primary: '', secondary: '', tertiary: '',
      idempotencyKey: 'c'.repeat(32), fileNames: [],
    }));
    const file = new File(['Version: 4.1.8'], 'sportarr-system-info.txt', { type: 'text/plain' });
    Object.defineProperty(file, 'text', { value: () => Promise.resolve('Version: 4.1.8') });
    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: {
      diagnosticFiles: [file], versionAndInstallType: '4.1.8 / Docker',
    } }]}><Routes><Route path="/support/new" element={<SupportNewIssuePage />} /></Routes></MemoryRouter>);

    expect(await screen.findByText('sportarr-system-info.txt')).toBeInTheDocument();
    expect(screen.getByLabelText('Example or alternative')).toHaveValue('');
  });

  it('prefills version for a question', async () => {
    localStorage.setItem('sportarr-support-issue-draft', JSON.stringify({
      category: 'question', title: '', primary: '', secondary: '', tertiary: '',
      idempotencyKey: 'd'.repeat(32), fileNames: [],
    }));
    render(<MemoryRouter initialEntries={[{ pathname: '/support/new', state: {
      versionAndInstallType: '4.1.8 / Docker',
    } }]}><Routes><Route path="/support/new" element={<SupportNewIssuePage />} /></Routes></MemoryRouter>);
    await waitFor(() => expect(screen.getByLabelText('Version and install type')).toHaveValue('4.1.8 / Docker'));
  });

  it('keeps selected files when the user briefly visits Logs', async () => {
    render(<MemoryRouter initialEntries={['/support/new']}><Routes>
      <Route path="/support/new" element={<><Link to="/logs">Open Logs</Link><SupportNewIssuePage /></>} />
      <Route path="/logs" element={<Link to="/support/new">Return to issue</Link>} />
    </Routes></MemoryRouter>);
    const file = new File(['Synthetic screenshot'], 'proof.txt', { type: 'text/plain' });

    fireEvent.change(screen.getByLabelText('Choose files'), { target: { files: [file] } });
    expect(screen.getByText('proof.txt')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('link', { name: 'Open Logs' }));
    fireEvent.click(screen.getByRole('link', { name: 'Return to issue' }));

    expect(screen.getByText('proof.txt')).toBeInTheDocument();
  });

  it('warns and blocks submission if a refreshed draft lost its files', async () => {
    localStorage.setItem('sportarr-support-issue-draft', JSON.stringify({
      category: 'bug', title: 'Missing attached log', primary: 'A synthetic issue with a saved log.',
      secondary: '', tertiary: '4.1.8 Docker', idempotencyKey: 'a'.repeat(32),
      fileNames: ['sportarr-support-01.txt'],
    }));
    render(<MemoryRouter initialEntries={['/support/new']}><Routes>
      <Route path="/support/new" element={<SupportNewIssuePage />} />
    </Routes></MemoryRouter>);

    expect(screen.getByText(/Files chosen earlier are not available/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Submit issue' }));
    expect(mocks.request).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Continue without missing files' }));
    expect(screen.queryByText(/Files chosen earlier are not available/)).not.toBeInTheDocument();
  });

  it('stops at the server limit of twelve files', () => {
    renderForm();
    const files = Array.from({ length: 13 }, (_, index) => new File(['synthetic'], `proof-${index + 1}.txt`));

    fireEvent.change(screen.getByLabelText('Choose files'), { target: { files } });

    expect(screen.getByText('proof-12.txt')).toBeInTheDocument();
    expect(screen.queryByText('proof-13.txt')).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('12 files');
  });
});
