import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { OperationPlan } from '../../features/workspace-operations/system-capabilities/SystemCapabilitySetup';
import type { SystemCapabilityOperation } from '../../features/workspace-operations/system-capabilities/systemCapabilityTypes';
import { systemCapabilityItem, systemSetupOperationFixture } from '../fixtures/systemCapabilityDetailSetup';

describe('Readable persisted capability operation plan', () => {
  it('uses immutable prepared labels after refresh rather than renamed live records', () => {
    // Arrange
    const operation: SystemCapabilityOperation = systemSetupOperationFixture();
    operation.plannedWrites = [{ ...operation.plannedWrites[0]!, displayLabel: 'Subscribe to reviewed Security monitoring (Cloud provider).' }];
    const changed = { ...systemCapabilityItem(), name: 'Changed live name', sourceRevision: 'newer' };
    // Act
    render(<OperationPlan operation={operation} records={[changed]} />);
    // Assert
    expect(screen.getByText('Subscribe to reviewed Security monitoring (Cloud provider).')).toBeVisible();
    expect(screen.queryByText('Changed live name')).not.toBeInTheDocument();
    expect(screen.queryByText(/names are unavailable/i)).not.toBeInTheDocument();
    expect(screen.getByText('subscription-a')).not.toBeVisible();
  });

  it('uses source-qualified selected names and collapses technical identifiers', () => {
    // Arrange
    const operation: SystemCapabilityOperation = systemSetupOperationFixture();
    const provider = systemCapabilityItem();
    const local = systemCapabilityItem('local');
    local.components[0]!.recordId = 'component-a';
    operation.selections.push({ ...operation.selections[0]!, source: 'local' });
    // Act
    render(<OperationPlan operation={operation} records={[local, provider]}
      boundaries={[{ id: 'boundary-a', name: 'Workload boundary' }]} systemName="Selected system" />);
    // Assert
    expect(screen.getByText('Subscribe provider capability')).toBeVisible();
    expect(screen.getByText('Security monitoring')).toBeVisible();
    expect(screen.getByText('Place component')).toBeVisible();
    expect(screen.getByText('Provider collector')).toBeVisible();
    expect(screen.queryByText('Response team')).not.toBeInTheDocument();
    expect(screen.getByText('Boundary: Workload boundary')).toBeVisible();
    expect(screen.getByText('subscription-a')).not.toBeVisible();
    expect(screen.getByText(/"sourceRevision": "source-1"/)).not.toBeVisible();
    const change = screen.getByText('Subscribe provider capability').closest('li')!;
    fireEvent.click(within(change).getByText('Details'));
    expect(within(change).getByText('subscription-a')).toBeVisible();
  });

  it.each([
    ['system-link', 'Link organization capability'],
    ['system-unlink', 'Unlink organization capability'],
    ['subscription', 'Subscribe provider capability'],
    ['unsubscribe', 'Unsubscribe provider capability'],
    ['support-link', 'Link supporting organization capability'],
    ['component-placement', 'Place component'],
    ['control-implementation', 'Create control implementation'],
    ['responsibility-reconciliation', 'Refresh responsibility review'],
    ['narrative-change', 'Queue narrative review'],
  ])('labels the persisted %s action without changing its exact payload', (writeKind, label) => {
    // Arrange
    const operation = systemSetupOperationFixture();
    operation.plannedWrites = [{ ...operation.plannedWrites[0]!, writeKind }];
    const original = JSON.stringify(operation);
    // Act
    render(<OperationPlan operation={operation} />);
    // Assert
    expect(screen.getByText(label)).toBeVisible();
    expect(JSON.stringify(operation)).toBe(original);
    expect(screen.getByText(writeKind)).not.toBeVisible();
  });

  it('does not pretend that old plans or unfamiliar write kinds have known names', () => {
    // Arrange
    const operation = systemSetupOperationFixture();
    operation.plannedWrites = [{ ...operation.plannedWrites[0]!, writeKind: 'future-kind' }];
    // Act
    render(<OperationPlan operation={operation} />);
    // Assert
    expect(screen.getByText('Unrecognized saved change')).toBeVisible();
    expect(screen.getByText(/names are unavailable.*details/i)).toBeVisible();
    expect(screen.getByText('future-kind')).not.toBeVisible();
  });

  it('rejects display names from source revisions other than the reviewed selection', () => {
    // Arrange
    const operation = systemSetupOperationFixture();
    const changed = { ...systemCapabilityItem(), sourceRevision: 'newer-source', name: 'Unreviewed rename' };
    // Act
    render(<OperationPlan operation={operation} records={[changed]} />);
    // Assert
    expect(screen.queryByText('Unreviewed rename')).not.toBeInTheDocument();
    expect(screen.getAllByText(/names are unavailable.*details/i)).toHaveLength(2);
  });

  it('distinguishes organization support from a component placement and retains explicit system-wide placement', () => {
    // Arrange
    const provider = systemCapabilityItem();
    const local = systemCapabilityItem('local', 'local-capability-a');
    const operation: SystemCapabilityOperation = systemSetupOperationFixture();
    operation.selections[0]!.supportingCapabilities = [{ recordId: local.recordId, sourceRevision: local.sourceRevision }];
    operation.plannedWrites = [
      { ...operation.plannedWrites[0]!, writeKind: 'support-link', componentId: local.recordId },
      { ...operation.plannedWrites[1]!, source: 'local', recordId: 'local-component-a', componentId: 'local-component-a', boundaryId: null },
    ];
    // Act
    render(<OperationPlan operation={operation} records={[provider, local]} />);
    // Assert
    expect(screen.getByText('Organization support: Incident response')).toBeVisible();
    expect(screen.getByText('Response team')).toBeVisible();
    expect(screen.getByText('Boundary: System-wide')).toBeVisible();
    expect(screen.queryByText('Boundary: Name unavailable; see Details')).not.toBeInTheDocument();
    expect(screen.getByText('Supporting capability ID')).not.toBeVisible();
  });
});
