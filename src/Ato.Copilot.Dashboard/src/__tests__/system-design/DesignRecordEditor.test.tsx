import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import DesignRecordEditor from '../../features/system-design/DesignRecordEditor';
import type { DesignEdge, DesignNode } from '../../api/systemDesign';

describe('Structured design relationship source selection', () => {
  it('captures SACA responsibility evidence and exact scope without treating a vault as TCCM', () => {
    // Arrange
    const node: DesignNode = { id: 'vdss', label: 'Recorded workload security', kind: 'DesignComponent',
      boundaryDisposition: 'Undetermined', reviewState: 'Draft', projectionStatus: 'Working', sspImpact: 'Deployment', properties: {} };
    const scope: DesignNode = { ...node, id: 'scope-a', kind: 'Environment', label: 'Recorded Government scope' };
    const apply = vi.fn();
    const rendered = render(<DesignRecordEditor node={node} nodes={[node, scope]} onApply={apply} onClose={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByLabelText('SACA deployment zone'), { target: { value: 'AzureCloud' } });
    fireEvent.change(screen.getByLabelText('SACA / SCCA role'), { target: { value: 'VDSS' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Recorded deployment scope' }), { target: { value: 'scope-a' } });
    fireEvent.change(screen.getByLabelText('Deployment responsibility / owner'), { target: { value: 'Recorded mission team' } });
    fireEvent.change(screen.getByLabelText('Deployment evidence reference URL'), { target: { value: 'https://example.invalid/inspection' } });
    fireEvent.change(screen.getByLabelText('Recorded deployment security functions'), { target: { value: 'Recorded traffic inspection / IAM / monitoring' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ sacaZone: 'AzureCloud', sacaRole: 'VDSS', deploymentScopeNodeId: 'scope-a',
      deploymentOwner: 'Recorded mission team', deploymentEvidenceReference: 'https://example.invalid/inspection',
      deploymentSecurityFunctions: 'Recorded traffic inspection / IAM / monitoring' }));
    expect(screen.getByLabelText('SACA / SCCA role').querySelector('option[value="TCCM"]')).toBeNull();
    rendered.unmount();
    // Arrange
    const performer = { ...node, id: 'tccm', kind: 'ActorGroup', sacaRole: 'TCCM' };
    render(<DesignRecordEditor node={performer} nodes={[performer]} onApply={apply} onClose={vi.fn()} />);
    // Assert
    expect(screen.getByLabelText('SACA / SCCA role')).toHaveValue('TCCM');
    expect(screen.getByLabelText('SACA / SCCA role').querySelector('option[value="BCAP"]')).toBeNull();
  });
  it('captures network component and interface annotations without inventing authorization', () => {
    // Arrange
    const node: DesignNode = { id: 'gateway', label: 'Recorded gateway', kind: 'DesignComponent',
      boundaryDisposition: 'Undetermined', reviewState: 'Draft', projectionStatus: 'Working', sspImpact: 'Network', properties: {} };
    const apply = vi.fn();
    const rendered = render(<DesignRecordEditor node={node} nodes={[node]} onApply={apply} onClose={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByLabelText('Network component role'), { target: { value: 'VpnGateway' } });
    fireEvent.change(screen.getByLabelText('Network segment / enclave'), { target: { value: 'Recorded DMZ' } });
    fireEvent.change(screen.getByLabelText('Network IP / CIDR address'), { target: { value: '10.40.0.0/24' } });
    fireEvent.change(screen.getByLabelText('Claimed hosting impact level'), { target: { value: 'IL5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ networkRole: 'VpnGateway', networkSegment: 'Recorded DMZ', networkAddress: '10.40.0.0/24', hostingImpactLevel: 'IL5' }));
    rendered.unmount();
    // Arrange
    const edge: DesignEdge = { id: 'flow', sourceNodeId: node.id, targetNodeId: node.id, relationshipType: 'DataFlow',
      direction: 'Outbound', boundaryCrossing: 'No', reviewState: 'Draft', projectionStatus: 'Working' };
    render(<DesignRecordEditor edge={edge} nodes={[node]} onApply={apply} onClose={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByLabelText('Protocol stack'), { target: { value: 'HTTPS / TLS 1.3 / TCP / IPv4' } });
    fireEvent.change(screen.getByLabelText('Standards profile reference URL'), { target: { value: 'https://example.invalid/standards' } });
    fireEvent.change(screen.getByLabelText('Connection medium'), { target: { value: 'DISN' } });
    fireEvent.change(screen.getByLabelText('Security control references'), { target: { value: 'SC-7' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(apply).toHaveBeenLastCalledWith(expect.objectContaining({ protocolStack: 'HTTPS / TLS 1.3 / TCP / IPv4',
      standardsReference: 'https://example.invalid/standards', connectionMedium: 'DISN', securityControlReferences: 'SC-7' }));
  });
  it('captures function/store handling and exact data-type selection as design annotations', () => {
    // Arrange
    const node: DesignNode = { id: 'store', label: 'Recorded store', kind: 'DataFlowElement',
      boundaryDisposition: 'InBoundary', reviewState: 'Draft', projectionStatus: 'Working', sspImpact: 'DFD', properties: {}, dataFlowRole: 'DataStore' };
    const apply = vi.fn();
    const rendered = render(<DesignRecordEditor node={node} nodes={[node]} onApply={apply} onClose={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByLabelText('Data retention', { exact: true }), { target: { value: 'Recorded retention' } });
    fireEvent.change(screen.getByLabelText('Data disposal / destruction method'), { target: { value: 'Recorded disposal' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ dataFlowRole: 'DataStore', dataRetention: 'Recorded retention', disposalMethod: 'Recorded disposal' }));
    rendered.unmount();
    // Arrange
    const information: DesignNode = { ...node, id: 'data', kind: 'InformationType', label: 'Recorded mission data',
      source: { type: 'DataTypeEntry', id: 'data-a', version: '1', provenance: 'Retained', reviewState: 'Approved', precedence: 3, resolutionUrl: '/data' },
      properties: { DataTypeName: 'Recorded mission data', SensitivityClassification: 'CUI' } };
    const edge: DesignEdge = { id: 'flow', sourceNodeId: 'store', targetNodeId: 'store', relationshipType: 'DataFlow',
      direction: 'Outbound', boundaryCrossing: 'No', reviewState: 'Draft', projectionStatus: 'Working' };
    render(<DesignRecordEditor edge={edge} nodes={[node, information]} onApply={apply} onClose={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByRole('combobox', { name: 'Recorded information type' }), { target: { value: 'data-a' } });
    fireEvent.change(screen.getByLabelText('Data lifecycle stage'), { target: { value: 'Store' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(apply).toHaveBeenLastCalledWith(expect.objectContaining({ informationTypeId: 'data-a', informationType: 'Recorded mission data',
      classification: 'CUI', lifecycleStage: 'Store' }));
  });
  it('captures actual logical type layer conditions effects and reference without fabricating provenance', () => {
    // Arrange
    const node: DesignNode = { id: 'goal', label: 'Mission availability', kind: 'LogicalConstruct',
      boundaryDisposition: 'Undetermined', reviewState: 'Draft', projectionStatus: 'Working', sspImpact: 'Logical architecture', properties: { logicalType: 'Goal' } };
    const apply = vi.fn();
    render(<DesignRecordEditor node={node} nodes={[node]} onApply={apply} onClose={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByLabelText('Abstraction layer'), { target: { value: 'Capability' } });
    fireEvent.change(screen.getByLabelText('Conditions'), { target: { value: 'Recorded operational condition' } });
    fireEvent.change(screen.getByLabelText('Desired effects'), { target: { value: 'Recorded mission effect' } });
    fireEvent.change(screen.getByLabelText('Source reference URL', { exact: true }), { target: { value: 'https://example.invalid/mission' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ properties: { logicalType: 'Goal', logicalLayer: 'Capability',
      conditions: 'Recorded operational condition', desiredEffect: 'Recorded mission effect', referenceUrl: 'https://example.invalid/mission' } }));
    expect(apply.mock.lastCall![0].source).toBeUndefined();
  });
  it('captures named boundary responsibility and rationale without changing canonical source facts', () => {
    // Arrange
    const asset: DesignNode = { id: 'asset', label: 'Recorded server', kind: 'DesignComponent',
      boundaryDisposition: 'Undetermined', reviewState: 'Draft', projectionStatus: 'Working', sspImpact: 'Boundary', properties: {} };
    const definition: DesignNode = { ...asset, id: 'definition', label: 'Mission production', kind: 'BoundaryDefinition',
      source: { type: 'BoundaryDefinition', id: 'scope-a', version: '1', provenance: 'Recorded', reviewState: 'Recorded',
        precedence: 7, resolutionUrl: '/systems/a/boundaries' } };
    const apply = vi.fn();
    render(<DesignRecordEditor node={asset} nodes={[asset, definition]} onApply={apply} onClose={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByLabelText('Named boundary scope'), { target: { value: 'scope-a' } });
    fireEvent.change(screen.getByLabelText('Boundary ownership relationship'), { target: { value: 'SystemManaged' } });
    fireEvent.change(screen.getByLabelText('Scope inclusion / exclusion rationale'), { target: { value: 'Recorded workload responsibility' } });
    fireEvent.change(screen.getByLabelText('Security responsibility'), { target: { value: 'Recorded operations team' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ boundaryDefinitionId: 'scope-a', boundaryRelationship: 'SystemManaged',
      boundaryRationale: 'Recorded workload responsibility', securityResponsibility: 'Recorded operations team' }));
  });
  it('records non-CSP performer context explicitly without creating an application role', () => {
    // Arrange
    const node: DesignNode = { id: 'performer', label: 'Configuration control board', kind: 'DesignComponent',
      boundaryDisposition: 'Undetermined', reviewState: 'Draft', projectionStatus: 'Working', sspImpact: 'System context', properties: {} };
    const apply = vi.fn();
    render(<DesignRecordEditor node={node} nodes={[node]} onApply={apply} onClose={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByLabelText('Context entity class'), { target: { value: 'Performer' } });
    fireEvent.change(screen.getByLabelText('Context category'), { target: { value: 'SecurityCompliance' } });
    fireEvent.change(screen.getByLabelText('Context role'), { target: { value: 'Configuration governance' } });
    fireEvent.change(screen.getByLabelText('Organization / authority'), { target: { value: 'Recorded mission organization' } });
    fireEvent.change(screen.getByLabelText('Supported operational activities'), { target: { value: 'Review proposed system changes' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ properties: {
      contextEntityClass: 'Performer', contextEntityCategory: 'SecurityCompliance', contextRole: 'Configuration governance',
      contextOrganization: 'Recorded mission organization', contextActivities: 'Review proposed system changes',
    } }));
    expect(apply.mock.lastCall![0].source).toBeUndefined();
  });
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
