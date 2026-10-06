import { describe, expect, it } from 'vitest';
import { identitySummary, userDocumentation } from '../../features/systems/userCategoryPresentation';

describe('Users documentation facts', () => {
  it('counts explicitly recorded fields and prioritizes an incomplete workload without fabricating authority', () => {
    // Arrange
    const rows = [{ id: 'human', categoryName: 'Recorded users' },
      { id: 'workload', categoryName: 'Recorded automation', identityType: 'WorkloadIdentity', privilegeLevel: 'Privileged',
        affiliation: 'Internal', accessMethod: 'API', authenticationMethod: 'Managed identity', userLocations: 'CONUS', authorizedDataTypes: 'Metadata' },
      { id: 'removed', categoryName: 'Removed', pendingDeletion: true }];
    // Act
    const status = userDocumentation(rows);
    // Assert
    expect(status).toEqual({ recorded: 9, total: 20, missing: 11, categories: 2, firstIncompleteId: 'workload', workloadName: 'Recorded automation' });
    expect(identitySummary(rows[1]!)).toBe('Workload identity · Privileged · Internal');
    expect(identitySummary({})).toBe('Not recorded · Not recorded · Not recorded');
  });
  it('does not count whitespace or copy mock readiness for an empty profile', () => {
    // Arrange / Act
    const empty = userDocumentation([]);
    const partial = userDocumentation([{ _tempId: 'local', categoryName: '  ', identityType: 'Human' }]);
    // Assert
    expect(empty.total).toBe(0);
    expect(partial.recorded).toBe(1);
    expect(partial.firstIncompleteId).toBe('local');
  });
});
