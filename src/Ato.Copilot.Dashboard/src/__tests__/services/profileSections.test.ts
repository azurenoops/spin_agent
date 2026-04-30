import { describe, expect, it } from 'vitest';
import { formatGovernanceStatusLabel, formatProfileSectionLabel } from '../../utils/profileSections';

describe('profileSections formatters', () => {
  it('formats profile section labels for user-facing copy', () => {
    expect(formatProfileSectionLabel('MissionAndPurpose')).toBe('Mission & Purpose');
    expect(formatProfileSectionLabel('PortsProtocolsAndServices')).toBe('Ports, Protocols & Services');
  });

  it('formats governance statuses into readable labels', () => {
    expect(formatGovernanceStatusLabel('NotStarted')).toBe('Not started');
    expect(formatGovernanceStatusLabel('UnderReview')).toBe('Under review');
    expect(formatGovernanceStatusLabel('NeedsRevision')).toBe('Needs revision');
  });
});
