import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import SystemPage from '../SystemPage';
import type { SystemStatus } from '../../types';

const status: SystemStatus = {
  appName: 'Sportarr', version: '4.1.8', buildTime: '2026-10-01T00:00:00Z',
  isDebug: false, isProduction: true, isDocker: true, runtimeVersion: '8.0',
  databaseType: 'SQLite', databaseVersion: '3', authentication: 'Forms',
  startTime: '2026-10-02T00:00:00Z', appData: '/private/home/user/appdata',
  osName: 'Linux', osVersion: '6', branch: 'dev', migrationVersion: 42, urlBase: '',
};

vi.mock('../../api/hooks', () => ({
  useSystemStatus: () => ({ data: status, isLoading: false, error: null }),
  useSystemHealth: () => ({ data: [], isPending: false, isError: false }),
}));

function Destination() {
  const location = useLocation();
  const file = (location.state as { diagnosticFiles?: File[] } | null)?.diagnosticFiles?.[0];
  const version = (location.state as { versionAndInstallType?: string } | null)?.versionAndInstallType;
  return <div><span>{file?.name}</span><span>{version}</span><span data-testid="file-content">{file ? 'attached' : 'missing'}</span></div>;
}

describe('System support handoff', () => {
  it('opens the issue form with a system-info file and version without submitting', async () => {
    render(<MemoryRouter initialEntries={['/system/status']}><Routes>
      <Route path="/system/status" element={<SystemPage />} />
      <Route path="/support/new" element={<Destination />} />
    </Routes></MemoryRouter>);

    fireEvent.click(screen.getByRole('button', { name: 'Create support issue' }));

    expect(await screen.findByText('sportarr-system-info.txt')).toBeInTheDocument();
    expect(screen.getByText('4.1.8 / Docker')).toBeInTheDocument();
    expect(screen.getByTestId('file-content')).toHaveTextContent('attached');
  });

  it('keeps private paths out of the attached system info', async () => {
    let attached: File | undefined;
    function Capture() {
      const location = useLocation();
      attached = (location.state as { diagnosticFiles?: File[] } | null)?.diagnosticFiles?.[0];
      return <div>Issue form</div>;
    }
    render(<MemoryRouter initialEntries={['/system/status']}><Routes>
      <Route path="/system/status" element={<SystemPage />} />
      <Route path="/support/new" element={<Capture />} />
    </Routes></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Create support issue' }));
    await waitFor(() => expect(attached).toBeDefined());
    const content = await new Promise<string>((resolve) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result));
      reader.readAsText(attached!);
    });
    expect(content).toContain('Version: 4.1.8');
    expect(content).not.toContain('/private/home/user/appdata');
    expect(content).not.toContain('Data Directory');
    expect(content).not.toContain('Forms');
    expect(content).not.toContain('2026-10-01T00:00:00Z');
    expect(content).not.toContain('2026-10-02T00:00:00Z');
  });
});
