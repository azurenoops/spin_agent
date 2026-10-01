import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import DesignRecordEditor from '../../features/system-design/DesignRecordEditor';
import type { DesignEdge, DesignNode } from '../../api/systemDesign';

describe('Structured design relationship source selection', () => {
  it('preserves canonical interconnection type and endpoints while allowing annotations', () => {
    // Arrange
    const nodes: DesignNode[] = ['system:a', 'external:a'].map(id => ({ id, label: id, kind: 'System',
      boundaryDisposition: 'Undetermined', reviewState: 'Draft', projectionStatus: 'Canonical', sspImpact: 'SSP', properties: {} }));
    const edge: DesignEdge = { id: 'edge-a', sourceNodeId: nodes[0]!.id, targetNodeId: nodes[1]!.id,
      relationshipType: 'Interconnection', direction: 'Outbound', boundaryCrossing: 'Yes',
      interconnectionId: 'interconnection-a', agreementStatus: 'Missing', reviewState: 'Draft', projectionStatus: 'Canonical',
      source: { type: 'SystemInterconnection', id: 'interconnection-a', version: '1', provenance: 'Canonical',
        reviewState: 'Unreviewed', precedence: 7, resolutionUrl: '/systems/a/profile/PortsProtocolsAndServices' } };
    const apply = vi.fn();
    render(<DesignRecordEditor edge={edge} nodes={nodes} onApply={apply} onClose={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByLabelText('Purpose'), { target: { value: 'Reviewed purpose' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(screen.getByLabelText('Relationship type')).toBeDisabled();
    expect(screen.getByLabelText('Relationship type')).toHaveValue('Interconnection');
    expect(screen.getByLabelText('Recorded interconnection')).toBeDisabled();
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ relationshipType: 'Interconnection',
      interconnectionId: 'interconnection-a', purpose: 'Reviewed purpose' }));
  });
  it('links an explicitly selected canonical PPS record and preserves its protocol and direction', () => {
    // Arrange
    const source = { type: 'PpsEntry', id: 'pps-a', version: 'rev-a', provenance: 'Reviewed source',
      reviewState: 'Approved', precedence: 3, resolutionUrl: '/systems/a/profile/PortsProtocolsAndServices' };
    const nodes: DesignNode[] = [
      { id: 'system:a', label: 'Mission', kind: 'System', boundaryDisposition: 'InBoundary', reviewState: 'Draft', projectionStatus: 'Working', sspImpact: 'SSP', properties: {} },
      { id: 'pps:a', label: 'Mission HTTPS', kind: 'PpsEntry', source, boundaryDisposition: 'Undetermined', reviewState: 'Approved',
        projectionStatus: 'ApprovedSnapshot', sspImpact: 'PPS', properties: { PortOrRange: '443', Protocol: 'TCP', Direction: 'Both', ServiceName: 'HTTPS' } },
    ];
    const edge: DesignEdge = { id: 'edge-a', sourceNodeId: 'system:a', targetNodeId: 'system:a', relationshipType: 'DataFlow',
      direction: 'Outbound', boundaryCrossing: 'No', reviewState: 'Draft', projectionStatus: 'Working' };
    const apply = vi.fn();
    render(<DesignRecordEditor edge={edge} nodes={nodes} onApply={apply} onClose={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByLabelText('Recorded PPS'), { target: { value: 'pps-a' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ ppsEntryId: 'pps-a', port: '443',
      protocol: 'TCP', service: 'HTTPS', direction: 'Bidirectional' }));
    expect(screen.queryByLabelText('Agreement status')).not.toBeInTheDocument();
  });
});
