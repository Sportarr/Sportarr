import { lazy, Suspense, type ComponentType } from 'react';
import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { BrowserRouter, Routes, Route, Link, useLocation, useNavigate } from 'react-router-dom';
import { useNavTarget, setNavTargetFromClick } from '../useNavTarget';

/**
 * React Router commits a location change inside a transition, and pages are
 * loaded lazily, so a highlight read from location.pathname cannot move until
 * the destination is ready. These tests pin the tab to the clicked path
 * instead, and prove the committed location still wins in the end.
 */

let resolvePage: (() => void) | null = null;

function makeSlowPage() {
  return lazy(
    () =>
      new Promise<{ default: ComponentType }>((resolve) => {
        resolvePage = () => resolve({ default: () => <div>slow page</div> });
      })
  );
}

function Nav() {
  const navPath = useNavTarget();
  return (
    <Link
      to="/slow"
      data-testid="tab"
      data-state={navPath === '/slow' ? 'active' : 'idle'}
      onClick={(e) => setNavTargetFromClick(e, '/slow')}
    >
      Slow
    </Link>
  );
}

function LocationOnlyNav() {
  const location = useLocation();
  const navigate = useNavigate();
  return (
    <>
      <span data-testid="committed">{location.pathname}</span>
      <button data-testid="go-home" onClick={() => navigate('/')}>
        home
      </button>
    </>
  );
}

function renderApp() {
  const SlowPage = makeSlowPage();
  return render(
    <BrowserRouter>
      <Nav />
      <LocationOnlyNav />
      <Suspense fallback={<div>loading</div>}>
        <Routes>
          <Route path="/" element={<div>home</div>} />
          <Route path="/slow" element={<SlowPage />} />
        </Routes>
      </Suspense>
    </BrowserRouter>
  );
}

describe('useNavTarget', () => {
  beforeEach(() => {
    window.history.pushState({}, '', '/');
    resolvePage = null;
  });

  it('highlights the clicked tab before the lazy page has resolved', () => {
    renderApp();
    expect(screen.getByTestId('tab')).toHaveAttribute('data-state', 'idle');

    fireEvent.click(screen.getByTestId('tab'));

    // The destination has NOT resolved yet - the committed location proves it.
    expect(screen.getByTestId('committed')).toHaveTextContent('/');
    // The tab has already answered the click.
    expect(screen.getByTestId('tab')).toHaveAttribute('data-state', 'active');
  });

  it('stays active once the page resolves and the router catches up', async () => {
    renderApp();
    fireEvent.click(screen.getByTestId('tab'));

    await act(async () => {
      resolvePage!();
    });

    await waitFor(() => {
      expect(screen.getByTestId('committed')).toHaveTextContent('/slow');
    });
    expect(screen.getByTestId('tab')).toHaveAttribute('data-state', 'active');
  });

  it('ignores a click that opens the link in another tab', () => {
    renderApp();

    // Ctrl, cmd, shift and middle clicks all leave this tab where it is, so
    // the router never navigates and nothing would clear a target set here.
    for (const modifier of [{ ctrlKey: true }, { metaKey: true }, { shiftKey: true }, { button: 1 }]) {
      fireEvent.click(screen.getByTestId('tab'), modifier);
      expect(screen.getByTestId('tab')).toHaveAttribute('data-state', 'idle');
    }

    expect(screen.getByTestId('committed')).toHaveTextContent('/');
  });

  it('lets a later navigation correct a click that never arrived', async () => {
    renderApp();

    // The click heads for /slow, which never resolves.
    fireEvent.click(screen.getByTestId('tab'));
    expect(screen.getByTestId('tab')).toHaveAttribute('data-state', 'active');

    // The user goes back to the page they were already on. The path does not
    // change, so only the new location object can clear the stale target.
    await act(async () => {
      fireEvent.click(screen.getByTestId('go-home'));
    });

    await waitFor(() => {
      expect(screen.getByTestId('tab')).toHaveAttribute('data-state', 'idle');
    });
    expect(screen.getByTestId('committed')).toHaveTextContent('/');
  });
});
