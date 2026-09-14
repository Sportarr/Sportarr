import { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import AcquisitionPolicyFields from '../AcquisitionPolicyFields';
import { acquisitionPolicyFrom, acquisitionPresets } from '../../utils/acquisitionPolicy';

function Harness() {
  const [value, setValue] = useState(acquisitionPolicyFrom({}));
  return <AcquisitionPolicyFields value={value} onChange={setValue} />;
}

describe('Acquisition policy controls', () => {
  it('applies the 14-day preset once and supports independent custom ages', () => {
    render(<Harness />);
    fireEvent.change(screen.getByLabelText('Acquisition preset'), { target: { value: 'recent' } });
    expect(screen.getByLabelText('Missing event age')).toHaveValue(14);
    expect(screen.getByLabelText('Upgrade event age')).toHaveValue(14);
    fireEvent.change(screen.getByLabelText('Missing event age'), { target: { value: '45' } });
    expect(screen.getByLabelText('Acquisition preset')).toHaveValue('custom');
    expect(screen.getByLabelText('Missing event age')).toHaveValue(45);
    expect(screen.getByLabelText('Upgrade event age')).toHaveValue(14);
  });

  it('allows manual-only and restores unlimited without retaining preset limits', () => {
    render(<Harness />);
    fireEvent.change(screen.getByLabelText('Acquisition preset'), { target: { value: 'manual' } });
    expect(screen.getByLabelText('Automatic missing downloads')).not.toBeChecked();
    expect(screen.getByLabelText('Automatic upgrades')).not.toBeChecked();
    expect(screen.getByLabelText('Missing event age')).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Acquisition preset'), { target: { value: 'unlimited' } });
    expect(screen.getByLabelText('Automatic missing downloads')).toBeChecked();
    expect(screen.getByLabelText('Missing event age')).toHaveValue(0);
  });

  it('preserves explicit false and zero when loading stored settings', () => {
    expect(acquisitionPolicyFrom(acquisitionPresets.manual)).toEqual(acquisitionPresets.manual);
    expect(acquisitionPolicyFrom({})).toEqual(acquisitionPresets.unlimited);
  });
});