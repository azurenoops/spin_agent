import { beforeEach, describe, expect, it, vi } from 'vitest';
import { saveProfileSection, reviewUserCategory, reviewSection } from '../../api/systemProfile';

const client = vi.hoisted(() => ({ put: vi.fn(), post: vi.fn(), get: vi.fn() }));
vi.mock('../../api/client', () => ({ default: client }));
beforeEach(() => vi.resetAllMocks());

describe('profile row persistence contract', () => {
  it('verifies the real section-review receipt then reads canonical profile data', async () => {
    // Arrange
    client.post.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', reviewScope: 'AccessContext', newStatus: 'Approved' } });
    const canonical = { sectionType: 'UsersAndAccess', governanceStatus: 'Approved', userCategories: [{ id: 'still-draft', governanceStatus: 'Draft' }] };
    client.get.mockResolvedValue({ data: canonical });
    // Act
    const result = await reviewSection('system-a', 'UsersAndAccess', { decision: 'approve' });
    // Assert
    expect(result).toEqual(canonical);
    expect(client.get).toHaveBeenCalledWith('/systems/system-a/profile/UsersAndAccess');
  });
  it('accepts a hidden row only when confirming its known pending removal', async () => {
    // Arrange
    client.post.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [],
      reviewResult: { categoryId: 'removed-a', action: 'approve', revision: 4, governanceStatus: 'Approved', pendingDeletion: true } } });
    // Act / Assert
    await expect(reviewUserCategory('system-a', 'removed-a', { action: 'approve', expectedRevision: 3 }, true)).resolves.toBeDefined();
    await expect(reviewUserCategory('system-a', 'removed-a', { action: 'approve', expectedRevision: 3 }))
      .rejects.toThrow('did not confirm the selected user-category review');
  });
  it('accepts retained approved rows awaiting removal review but not unrequested active rows', async () => {
    // Arrange
    client.put.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [
      { id: 'retained-row', pendingDeletion: true, governanceStatus: 'Draft', approvedSnapshotId: 'snapshot-a' },
    ] } });
    client.get.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [
      { id: 'retained-row', pendingDeletion: true, governanceStatus: 'Draft', approvedSnapshotId: 'snapshot-a' },
    ] } });
    // Act / Assert
    expect((await saveProfileSection('system-a', 'UsersAndAccess', { content: '{}', childItems: [] })).userCategories).toHaveLength(1);
    client.put.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [{ id: 'retained-row', pendingDeletion: false }] } });
    client.get.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [{ id: 'retained-row', pendingDeletion: false }] } });
    await expect(saveProfileSection('system-a', 'UsersAndAccess', { content: '{}', childItems: [] }))
      .rejects.toThrow('did not confirm the submitted profile rows');
  });
  it('requires the selected row and its new revision to confirm review', async () => {
    // Arrange
    const receipt = { categoryId: 'user-a', action: 'approve', revision: 4, governanceStatus: 'Approved', pendingDeletion: false };
    client.post.mockResolvedValue({ data: { reviewResult: receipt, userCategories: [{ id: 'user-a', governanceStatus: 'Approved', revision: 4 }] } });
    // Act
    await reviewUserCategory('system-a', 'user-a', { action: 'approve', expectedRevision: 3 });
    // Assert
    expect(client.post).toHaveBeenCalledWith('/systems/system-a/profile/UsersAndAccess/user-categories/user-a/review',
      { action: 'approve', expectedRevision: 3 });
    client.post.mockResolvedValue({ data: { reviewResult: receipt, userCategories: [{ id: 'user-a', governanceStatus: 'Approved', revision: 3 }] } });
    await expect(reviewUserCategory('system-a', 'user-a', { action: 'approve', expectedRevision: 3 }))
      .rejects.toThrow('did not confirm the selected user-category review');
  });
  it('does not treat a sibling approval or unconfirmed status as the selected row decision', async () => {
    // Arrange
    client.post.mockResolvedValue({ data: { userCategories: [
      { id: 'user-b', governanceStatus: 'Approved', revision: 9 },
      { id: 'user-a', governanceStatus: 'UnderReview', revision: 4 },
    ] } });
    // Act / Assert
    await expect(reviewUserCategory('system-a', 'user-a', { action: 'approve', expectedRevision: 3 }))
      .rejects.toThrow('did not confirm the selected user-category review');
  });
  it('rejects the old success-shaped response that drops submitted rows', async () => {
    // Arrange
    client.put.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [] } });
    // Act / Assert
    await expect(saveProfileSection('system-a', 'UsersAndAccess', {
      content: '{}', childItems: [{ categoryName: 'Mission staff', approximateCount: 120 }],
    })).rejects.toThrow('did not confirm the submitted profile rows');
  });
  it('accepts canonical generated IDs but not altered population data', async () => {
    // Arrange
    const row = { id: 'saved-a', categoryName: 'Mission staff', approximateCount: 120, description: null, sortOrder: 0 };
    client.put.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [row] } });
    const request = { content: '{}', childItems: [{ _tempId: 'local-a', categoryName: 'Mission staff', approximateCount: 120, description: '', sortOrder: 0 }] };
    // Act / Assert
    expect((await saveProfileSection('system-a', 'UsersAndAccess', request)).userCategories).toEqual([row]);
    client.put.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [{ ...row, approximateCount: 0 }] } });
    await expect(saveProfileSection('system-a', 'UsersAndAccess', request)).rejects.toThrow('did not confirm the submitted profile rows');
  });
  it.each([
    ['DataTypes', 'dataTypeEntries', { dataTypeName: 'Operational Data' }],
    ['PortsProtocolsAndServices', 'ppsEntries', { portOrRange: '443' }],
    ['LeveragedAuthorizations', 'leveragedAuthorizations', { providerName: 'Provider' }],
  ] as const)('checks %s rows too', async (sectionType, collection, row) => {
    // Arrange
    client.put.mockResolvedValue({ data: { sectionType, [collection]: [] } });
    // Act / Assert
    await expect(saveProfileSection('system-a', sectionType, { content: '{}', childItems: [row] }))
      .rejects.toThrow('did not confirm the submitted profile rows');
  });
  it('preserves omitted-array semantics and verifies explicit deletion', async () => {
    // Arrange
    client.put.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [{ id: 'existing' }] } });
    // Act / Assert
    await expect(saveProfileSection('system-a', 'UsersAndAccess', { content: '{}' })).resolves.toBeDefined();
    client.put.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [] } });
    client.get.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [{ id: 'existing' }] } });
    await expect(saveProfileSection('system-a', 'UsersAndAccess', { content: '{}', childItems: [] }))
      .rejects.toThrow('did not confirm the submitted profile rows');
    expect(client.get).toHaveBeenCalledWith('/systems/system-a/profile/UsersAndAccess');
  });
  it('normalizes display order without changing submitted business fields or revisions', async () => {
    // Arrange
    const input = { id: 'user-a', categoryName: 'Staff', sortOrder: 9, revision: 3 };
    client.put.mockResolvedValue({ data: { sectionType: 'UsersAndAccess', userCategories: [{ ...input, sortOrder: 0 }] } });
    // Act
    await saveProfileSection('system-a', 'UsersAndAccess', { content: '{}', childItems: [input] });
    // Assert
    expect(client.put).toHaveBeenCalledWith('/systems/system-a/profile/UsersAndAccess', { content: '{}', childItems: [{ ...input, sortOrder: 0 }] });
    expect(input.sortOrder).toBe(9);
  });
});
