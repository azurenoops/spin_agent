import '../helpers/recharts';
import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import AtoStatusChart from '../../features/csp-dashboard/widgets/AtoStatusChart';
import FindingsBySeverityChart from '../../features/csp-dashboard/widgets/FindingsBySeverityChart';

describe('Provider portfolio graphical summaries', () => {
  it('retains exact provider decision and severity totals in accessible shared graph cards', () => {
    // Arrange / Act
    render(<>
      <AtoStatusChart counts={{ authorized: 2, inProcess: 3, denied: 1 }} />
      <FindingsBySeverityChart counts={{ critical: 1, high: 2, moderate: 3, low: 4 }} openPoamCount={5} openDeviationCount={2} />
    </>);
    // Assert
    expect(within(screen.getByRole('region', { name: 'ATO status across organizations' })).getByRole('img'))
      .toHaveAccessibleName(/Authorized: 2.*In Process: 3.*Denied: 1/);
    expect(within(screen.getByRole('region', { name: 'Open findings by severity' })).getByRole('img'))
      .toHaveAccessibleName(/Critical: 1.*High: 2.*Moderate: 3.*Low: 4/);
    expect(screen.getByText('10 findings · 5 open POA&Ms · 2 open deviations')).toBeInTheDocument();
    expect(document.querySelectorAll('.recharts-wrapper > svg.recharts-surface')).toHaveLength(2);
  });

  it('renders zero graphs with explicit empty messages, not illustrative bars', () => {
    // Arrange / Act
    render(<>
      <AtoStatusChart counts={{ authorized: 0, inProcess: 0, denied: 0 }} />
      <FindingsBySeverityChart counts={{ critical: 0, high: 0, moderate: 0, low: 0 }} openPoamCount={0} openDeviationCount={0} />
    </>);
    // Assert
    expect(screen.getByText('No ATO decisions recorded')).toBeInTheDocument();
    expect(screen.getByText('No open findings')).toBeInTheDocument();
    expect(document.querySelectorAll('.recharts-wrapper > svg.recharts-surface')).toHaveLength(2);
  });
});
