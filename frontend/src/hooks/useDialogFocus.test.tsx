import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { useDialogFocus } from './useDialogFocus';

function Dialog() {
  const ref = useDialogFocus(true, vi.fn());
  return <div ref={ref} role="dialog">
    <a href="https://github.com/Sportarr/Sportarr/issues/326">View issue on GitHub</a>
    <button type="button">Start another reply</button>
  </div>;
}

describe('useDialogFocus', () => {
  it('includes the GitHub link in the dialog focus order', () => {
    render(<Dialog />);

    const link = screen.getByRole('link', { name: 'View issue on GitHub' });
    const button = screen.getByRole('button', { name: 'Start another reply' });
    expect(link).toHaveFocus();

    button.focus();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(link).toHaveFocus();
  });
});
