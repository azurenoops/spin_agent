import { useState } from 'react';
import type { DesignEdge, DesignNode } from '../../api/systemDesign';
import SetupDialog from '../workspace-operations/SetupDialog';
import { isArchitectureNode, isContextConstraint, isNonTechnicalInteraction } from './graphAdapter';
import { LOGICAL_TYPES, LOGICAL_PREDICATES, logicalType } from './graphAdapter';
import { DFD_ROLES, DATA_LIFECYCLE, isFlowEndpoint } from './graphAdapter';
import { NETWORK_ROLES, NETWORK_MEDIA, isNetworkComponent } from './graphAdapter';
import { SACA_ZONES, SACA_ROLES, isContextPerformer } from './graphAdapter';

export default function DesignRecordEditor({ node, edge, nodes, edges = [], onApply, onClose }: {
  node?: DesignNode; edge?: DesignEdge; nodes: DesignNode[]; edges?: DesignEdge[];
  onApply: (record: DesignNode | DesignEdge) => void; onClose: () => void;
}) {
  const [draft, setDraft] = useState<DesignNode | DesignEdge>(() => structuredClone(node ?? edge!));
  const [error, setError] = useState('');
  const canonicalConnection = edge?.source?.type === 'SystemInterconnection';
  const logical = 'sourceNodeId' in draft && LOGICAL_PREDICATES.includes(draft.relationshipType);
  const nonTechnical = 'sourceNodeId' in draft && ['GovernanceInteraction', 'ConstraintReference', ...LOGICAL_PREDICATES].includes(draft.relationshipType);
  const required = new Set(['label', 'sourceNodeId', 'targetNodeId', 'boundaryDisposition', 'relationshipType', 'direction', 'boundaryCrossing', 'sspImpact']);
  const set = (key: string, value: string) => setDraft(current => ({ ...current, [key]: value || (required.has(key) ? '' : null) }));
  const field = (label: string, key: string, options?: string[]) => <label className="sd-field" key={key}>{label}
    {options ? <select required={required.has(key)} value={String((draft as unknown as Record<string, unknown>)[key] ?? '')} onChange={event => set(key, event.target.value)}>
      <option value="">Not recorded</option>{options.map(option => <option key={option}>{option}</option>)}
    </select> : <input required={key === 'label'} value={String((draft as unknown as Record<string, unknown>)[key] ?? '')} onChange={event => set(key, event.target.value)} />}
  </label>;
  const contextField = (label: string, key: string, options?: string[]) => {
    if (!('label' in draft)) return null;
    const update = (value: string) => setDraft(current => {
      if (!('label' in current)) return current;
      const properties = { ...current.properties };
      if (value) properties[key] = value; else delete properties[key];
      return { ...current, properties };
    });
    return <label className="sd-field" key={key}>{label}{options
      ? <select disabled={!!node?.source} value={draft.properties[key] ?? ''} onChange={event => update(event.target.value)}>
        <option value="">Not recorded</option>{options.map(value => <option key={value}>{value}</option>)}
      </select>
      : <input disabled={!!node?.source} maxLength={4000} value={draft.properties[key] ?? ''} onChange={event => update(event.target.value)} />}</label>;
  };
  return <SetupDialog title={node ? 'Edit design element' : logical ? 'Edit logical relationship' : nonTechnical ? 'Edit context interaction' : 'Edit data flow'} busy={false} onClose={onClose}
    description="Stage a governed correction. Renaming changes the design label, not the canonical source record. Canonical provenance remains attached; saving a draft is not approval.">
    <form className="sd-editor" onSubmit={event => {
      event.preventDefault();
      if ('label' in draft && !draft.label.trim()) return setError('A label is required.');
      if ('sourceNodeId' in draft && (!nodes.some(item => item.id === draft.sourceNodeId) || !nodes.some(item => item.id === draft.targetNodeId)))
        return setError('Select recorded source and destination elements.');
      onApply(draft);
    }}>
      {node ? <>
        {field('Label', 'label')}
        {field('Boundary disposition', 'boundaryDisposition', ['InBoundary', 'OutOfBoundary', 'Undetermined'])}
        <label className="sd-field">Named boundary scope
          <select value={'label' in draft ? draft.boundaryDefinitionId ?? '' : ''} onChange={event => set('boundaryDefinitionId', event.target.value)}>
            <option value="">Not selected</option>
            {nodes.filter(item => item.kind === 'BoundaryDefinition' && item.source?.id).map(item =>
              <option key={item.id} value={item.source!.id}>{item.label}</option>)}
            {'label' in draft && draft.boundaryDefinitionId && !nodes.some(item => item.kind === 'BoundaryDefinition' && item.source?.id === draft.boundaryDefinitionId)
              && <option value={draft.boundaryDefinitionId}>Recorded selection unavailable — reconcile source</option>}
          </select>
        </label>
        {field('Boundary ownership relationship', 'boundaryRelationship', ['SystemManaged', 'SharedService', 'SeparatelyAuthorized', 'Undetermined'])}
        {field('Scope inclusion / exclusion rationale', 'boundaryRationale')}
        {field('Security responsibility', 'securityResponsibility')}
        {field('External authorization/source reference URL', 'externalAuthorizationReference')}
        <p className="sd-muted">These are governed design annotations, not changes to canonical scope or an AO decision. Shared/separately authorized systems stay outside. A recorded system decision does not automatically verify this component's authorization coverage.</p>
        {field('Environment', 'environment')}{field('Network / trust zone', 'networkZone')}{field('Provider', 'provider')}
        {field('SSP impact', 'sspImpact')}
        {isArchitectureNode(node) && <>
          {field('SACA deployment zone', 'sacaZone', SACA_ZONES)}
          {field('SACA / SCCA role', 'sacaRole', 'label' in draft && isContextPerformer(draft)
            ? ['TCCM', 'Undetermined'] : SACA_ROLES.filter(role => role !== 'TCCM'))}
          <label className="sd-field">Recorded deployment scope
            <select aria-label="Recorded deployment scope" value={'label' in draft ? draft.deploymentScopeNodeId ?? '' : ''} onChange={event => set('deploymentScopeNodeId', event.target.value)}>
              <option value="">No explicit scope selected</option>
              {nodes.filter(n => n.kind === 'Environment').map(n => <option key={n.id} value={n.id}>{n.label}</option>)}
              {'label' in draft && draft.deploymentScopeNodeId && !nodes.some(n => n.kind === 'Environment' && n.id === draft.deploymentScopeNodeId)
                && <option value={draft.deploymentScopeNodeId}>Recorded scope unavailable - reconcile source</option>}
            </select>
          </label>
          {field('Deployment responsibility / owner', 'deploymentOwner')}
          {field('Deployment evidence reference URL', 'deploymentEvidenceReference')}
          {field('Recorded deployment security functions', 'deploymentSecurityFunctions')}
          <p className="sd-muted">Record actual protection at rest/in transit, isolation, IAM/RBAC and monitoring functions with source evidence. TCCM is an AO-appointed business performer, not Key Vault or another appliance. SACA/CNAP labels and CSP links do not establish compliance, appointment, implementation or authorization.</p>
        </>}
        {isNetworkComponent(node) && <>
          {field('Network component role', 'networkRole', NETWORK_ROLES)}
          {field('Network segment / enclave', 'networkSegment')}
          {field('Network IP / CIDR address', 'networkAddress')}
          {field('Claimed hosting impact level', 'hostingImpactLevel', ['IL2', 'IL3', 'IL4', 'IL5', 'IL6'])}
          <p className="sd-muted">Reuse canonical inventory addresses where recorded. These annotations do not establish IL accreditation, accepted inheritance, inventory completeness or authorization. Source and evidence remain reviewable separately.</p>
        </>}
        {(isArchitectureNode(node) || node.kind === 'DataFlowElement'
          || node.kind === 'LogicalConstruct' && node.properties.logicalType === 'Activity') && <>
          {field('DFD role', 'dataFlowRole', node.kind === 'LogicalConstruct' ? ['Function', 'Undetermined'] : DFD_ROLES)}
          {field('System function / transformation description', 'functionDescription')}
          {field('Data retention', 'dataRetention')}
          {field('Data disposal / destruction method', 'disposalMethod')}
          <p className="sd-muted">These are reviewed design annotations, not canonical record changes. Record actual functions, stores and external data producers/consumers; hosting/CSP links do not establish a data exchange.</p>
        </>}
        {node.kind === 'LogicalConstruct' ? <>
          {contextField('Logical construct type', 'logicalType', LOGICAL_TYPES)}
          {contextField('Abstraction layer', 'logicalLayer', ['Capability', 'Operational', 'System', 'Implementation'])}
          {contextField('Construct description', 'description')}
          {contextField('Conditions', 'conditions')}
          {contextField('Desired effects', 'desiredEffect')}
          {contextField('Source reference URL', 'referenceUrl')}
          <p className="sd-muted">Record actual facts and explicit predicates. A security measure is not automatically a mission capability. Use Realizes to document refinement; do not infer it from names or hosting.</p>
        </> : node.kind === 'ContextConstraint' ? <>
          {contextField('Reference type', 'referenceType', ['Law', 'Regulation', 'Policy', 'Standard', 'Architecture reference'])}
          {contextField('Organization / authority', 'contextOrganization')}
          {contextField('Source reference URL', 'referenceUrl')}
          {contextField('Applicability rationale', 'rationale')}
          <p className="sd-muted">A reference records an applicable constraint for review, not a communicating system or an approval of applicability.</p>
        </> : <>
          {contextField('Context entity class', 'contextEntityClass', ['Performer', 'System'])}
          {contextField('Context category', 'contextEntityCategory', ['Operational', 'SecurityCompliance', 'DataSource', 'SupportService'])}
          {contextField('Context role', 'contextRole')}
          {contextField('Organization / authority', 'contextOrganization')}
          {contextField('Supported operational activities', 'contextActivities')}
          {contextField('Entity/source reference URL', 'referenceUrl')}
          <p className="sd-muted">Context describes the entity, not an application role grant. CSP linkage comes from canonical hosting/provider records, not the Provider text field. Canonical context facts must be updated in their source workflow.</p>
        </>}
      </> : <>
        {!nonTechnical && <label className="sd-field">Recorded information type
          <select aria-label="Recorded information type" value={'sourceNodeId' in draft ? draft.informationTypeId ?? '' : ''} onChange={event => {
            const information = nodes.find(n => n.kind === 'InformationType' && n.source?.id === event.target.value);
            setDraft(current => !('sourceNodeId' in current) ? current : { ...current, informationTypeId: information?.source?.id ?? null,
              ...(information ? { informationType: information.properties.DataTypeName ?? information.label,
                classification: information.properties.SensitivityClassification } : {}) });
          }}>
            <option value="">No recorded information type selected</option>
            {nodes.filter(n => n.kind === 'InformationType' && n.source).map(n => <option key={n.id} value={n.source!.id}>{n.label}</option>)}
            {'sourceNodeId' in draft && draft.informationTypeId && !nodes.some(n => n.kind === 'InformationType' && n.source?.id === draft.informationTypeId)
              && <option value={draft.informationTypeId}>Selected source unavailable - reconcile before review</option>}
          </select>
        </label>}
        <label className="sd-field">Recorded PPS
          <select value={(draft as DesignEdge).ppsEntryId ?? ''} onChange={event => {
            const record = nodes.find(item => item.kind === 'PpsEntry' && item.source?.id === event.target.value);
            if (!record) { set('ppsEntryId', ''); return; }
            const direction = record.properties.Direction === 'Both' ? 'Bidirectional' : record.properties.Direction;
            setDraft(current => !('sourceNodeId' in current) ? current : ({ ...current, ppsEntryId: record.source!.id,
              port: record.properties.PortOrRange, protocol: record.properties.Protocol,
              service: record.properties.ServiceName,
              direction: !canonicalConnection && (direction === 'Inbound' || direction === 'Outbound' || direction === 'Bidirectional') ? direction : current.direction }));
          }}>
            <option value="">No recorded PPS selected</option>
            {nodes.filter(item => item.kind === 'PpsEntry' && item.source).map(item =>
              <option key={item.id} value={item.source!.id}>{item.label} · {item.properties.PortOrRange} / {item.properties.Protocol}</option>)}
          </select>
        </label>
        {(['sourceNodeId', 'targetNodeId'] as const).map(key => <label key={key} className="sd-field">{key === 'sourceNodeId' ? 'Source element' : 'Destination element'}
          <select required disabled={canonicalConnection} value={(draft as DesignEdge)[key]} onChange={event => {
            setDraft(current => ({ ...current, [key]: event.target.value, interconnectionId: null, agreementStatus: null }));
          }}>
            <option value="">Choose recorded element</option>            {nodes.filter(item => logical ? logicalType(item) !== undefined && logicalType(item) !== 'ScopeReference' : isFlowEndpoint(item)
              || (draft as DesignEdge).relationshipType === 'ConstraintReference' && isContextConstraint(item)).map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
          </select></label>)}
        {canonicalConnection ? <>
          <label className="sd-field">Relationship type<select disabled value={edge.relationshipType}><option value={edge.relationshipType}>{edge.relationshipType}</option></select></label>
          <label className="sd-field">Direction<select disabled value={edge.direction}><option value={edge.direction}>{edge.direction}</option></select></label>
        </> : <>
          {field('Relationship type', 'relationshipType', ['DataFlow', 'ServiceFlow', 'ResourceFlow', 'Access', 'Dependency', 'NetworkConnection', 'GovernanceInteraction', 'ConstraintReference', ...LOGICAL_PREDICATES])}
          {field('Direction', 'direction', logical ? ['Outbound'] : ['Outbound', 'Inbound', 'Bidirectional'])}
        </>}
        {field('Purpose', 'purpose')}{field('Information type', 'informationType')}{field('Classification', 'classification')}
        {!nonTechnical && field('Data lifecycle stage', 'lifecycleStage', DATA_LIFECYCLE)}
        {!nonTechnical && <>
          {field('Protocol stack', 'protocolStack')}
          {field('Standards profile reference URL', 'standardsReference')}
          {field('Connection medium', 'connectionMedium', NETWORK_MEDIA)}
          {field('Security control references', 'securityControlReferences')}
          <p className="sd-muted">Record the actual stack and standards source; control IDs and DISN labels are not approval or implementation evidence. Existing PPS and interconnection checks still apply.</p>
        </>}
        {field('Port', 'port')}{field('Protocol', 'protocol')}{field('Service', 'service')}{field('Protection mechanism', 'protection')}
        {field('Encryption state', 'encryptionState', ['Encrypted', 'Unencrypted', 'Unknown'])}
        {field('Boundary crossing', 'boundaryCrossing', ['Yes', 'No', 'Unknown'])}
        {nonTechnical && <p className="sd-muted">Record the non-technical logical, governance or constraint purpose. Do not enter protocol, port, encryption, PPS or interconnection fields or relabel an existing technical flow to avoid review. Record a separate technical exchange when transport is actually known.</p>}
        <label className="sd-field">Recorded interconnection
          <select disabled={canonicalConnection} value={(draft as DesignEdge).interconnectionId ?? ''} onChange={event => {
            const record = edges.find(item => item.interconnectionId === event.target.value);
            setDraft(current => ({ ...current, interconnectionId: record?.interconnectionId ?? null,
              agreementStatus: record?.agreementStatus ?? null }));
          }}>
            <option value="">No recorded interconnection selected</option>
            {[...new Map([...edges, ...(edge ? [edge] : [])].filter(item => item.interconnectionId && (canonicalConnection
              || !isNonTechnicalInteraction(draft as DesignEdge) && item.sourceNodeId === (draft as DesignEdge).sourceNodeId && item.targetNodeId === (draft as DesignEdge).targetNodeId
              || item.direction === 'Bidirectional' && item.sourceNodeId === (draft as DesignEdge).targetNodeId && item.targetNodeId === (draft as DesignEdge).sourceNodeId))
              .map(item => [item.interconnectionId, item])).values()].map(item =>
              <option key={item.id} value={item.interconnectionId!}>{item.purpose ?? item.relationshipType} · {item.agreementStatus ?? 'Agreement not recorded'}</option>)}
          </select>
        </label>
        <p className="sd-muted">Agreement status comes from the canonical interconnection, not a design edit. Missing or expired agreements must be resolved in Ports &amp; interconnections.</p>
      </>}
      {error && <p role="alert">{error}</p>}
      <p className="sd-muted">Incomplete fields may be saved as a partial draft. Server validation and readiness determine whether review and approval are allowed.</p>
      <div className="sd-actions"><button type="button" className="sd-button" onClick={onClose}>Cancel</button>
        <button type="submit" className="sd-button sd-primary">Apply to draft</button></div>
    </form>
  </SetupDialog>;
}
