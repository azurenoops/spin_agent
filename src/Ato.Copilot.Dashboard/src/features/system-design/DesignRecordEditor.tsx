import { useState } from 'react';
import type { DesignEdge, DesignGroup, DesignNode } from '../../api/systemDesign';
import SetupDialog from '../workspace-operations/SetupDialog';
import { Link } from '../workspaces/workspaceNavigation';
import { isArchitectureNode, isContextConstraint, isNonTechnicalInteraction } from './graphAdapter';
import { LOGICAL_TYPES, LOGICAL_PREDICATES, logicalType } from './graphAdapter';
import { DFD_ROLES, DATA_LIFECYCLE, isFlowEndpoint } from './graphAdapter';
import { NETWORK_ROLES, NETWORK_MEDIA, isNetworkComponent } from './graphAdapter';
import { SACA_ZONES, SACA_ROLES, isContextPerformer } from './graphAdapter';

export default function DesignRecordEditor({ node, edge, nodes, edges = [], groups = [], inventoryOnly = false, onApply, onClose }: {
  node?: DesignNode; edge?: DesignEdge; nodes: DesignNode[]; edges?: DesignEdge[];
  groups?: DesignGroup[];
  inventoryOnly?: boolean;
  onApply: (record: DesignNode | DesignEdge) => void; onClose: () => void;
}) {
  const [draft, setDraft] = useState<DesignNode | DesignEdge>(() => structuredClone(node && inventoryOnly
    ? { ...node, deploymentOwner: node.deploymentOwner ?? node.properties.Owner,
      environment: node.environment ?? node.properties.Environment } : node ?? edge!));
  const [error, setError] = useState('');
  const canonicalConnection = edge?.source?.type === 'SystemInterconnection';
  const logical = 'sourceNodeId' in draft && LOGICAL_PREDICATES.includes(draft.relationshipType);
  const nonTechnical = 'sourceNodeId' in draft && ['GovernanceInteraction', 'ConstraintReference', ...LOGICAL_PREDICATES].includes(draft.relationshipType);
  const required = new Set(['label', 'sourceNodeId', 'targetNodeId', 'boundaryDisposition', 'relationshipType', 'direction', 'boundaryCrossing', 'sspImpact']);
  const set = (key: string, value: string) => setDraft(current => ({ ...current, [key]: value || (required.has(key) ? '' : null) }));
  const field = (label: string, key: string, options?: string[]) => <label className="sd-field" key={key}>{label}
    {options ? <select required={required.has(key)} value={String((draft as unknown as Record<string, unknown>)[key] ?? '')} onChange={event => set(key, event.target.value)}>
      <option value="">Not recorded</option>
      {String((draft as unknown as Record<string, unknown>)[key] ?? '')
        && !options.includes(String((draft as unknown as Record<string, unknown>)[key]))
        && <option value={String((draft as unknown as Record<string, unknown>)[key])}>Unknown recorded value: {String((draft as unknown as Record<string, unknown>)[key])}</option>}
      {options.map(option => <option key={option} value={option}>
        {inventoryOnly ? option.replace(/([a-z])([A-Z])/g, '$1 $2') : option}</option>)}
    </select> : <input required={key === 'label'} maxLength={key === 'label' || key === 'deploymentOwner' ? 500
      : key === 'externalAuthorizationReference' ? 2000 : 4000}
      value={String((draft as unknown as Record<string, unknown>)[key] ?? '')} onChange={event => set(key, event.target.value)} />}
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
  return <SetupDialog title={node ? inventoryOnly ? 'Edit inventory component' : 'Edit design element' : logical ? 'Edit logical relationship' : nonTechnical ? 'Edit context interaction' : 'Edit data flow'} busy={false} onClose={onClose}
    description="Stage a governed correction. Renaming changes the design label, not the canonical source record. Canonical provenance remains attached; saving a draft is not approval.">
    <form className="sd-editor" onSubmit={event => {
      event.preventDefault();
      if ('label' in draft && !draft.label.trim()) return setError('A label is required.');
      if (inventoryOnly && 'label' in draft) {
        if (!['InBoundary', 'OutOfBoundary', 'Undetermined'].includes(draft.boundaryDisposition))
          return setError('Choose a supported scope decision. Unknown recorded values cannot be saved unchanged.');
        if (draft.boundaryDisposition === 'InBoundary' && (['SharedService', 'SeparatelyAuthorized'].includes(draft.boundaryRelationship ?? '')
          || draft.kind === 'ExternalSystem' && draft.source?.type === 'SystemInterconnection'))
          return setError(`${draft.boundaryRelationship || 'Canonical external system'} must be outside this system under the recorded contract. Correct the decision or relationship explicitly; provider hosting alone does not require exclusion.`);
      }
      if ('sourceNodeId' in draft && (!nodes.some(item => item.id === draft.sourceNodeId) || !nodes.some(item => item.id === draft.targetNodeId)))
        return setError('Select recorded source and destination elements.');
      onApply(draft);
    }}>
      {node ? <>
        {field(inventoryOnly ? 'Component name' : 'Label', 'label')}
        {inventoryOnly && 'label' in draft && <>
          <label className="sd-field">Does this component belong to this system?
            <select required value={draft.boundaryDisposition} onChange={event => set('boundaryDisposition', event.target.value)}>
              {!['InBoundary', 'OutOfBoundary', 'Undetermined'].includes(draft.boundaryDisposition)
                && <option value={draft.boundaryDisposition}>Unknown recorded decision: {draft.boundaryDisposition || '(empty)'}</option>}
              <option value="InBoundary">Included in this system</option>
              <option value="OutOfBoundary">Outside this system</option>
              <option value="Undetermined">Needs confirmation</option>
            </select>
          </label>
          {field('Who operates or manages this component?', 'deploymentOwner')}
          {field('Inclusion / exclusion rationale', 'boundaryRationale')}
          {field('Security responsibility', 'securityResponsibility')}
          <section className="sd-muted col-span-full">
            <h3>Which recorded system area does it support, when applicable?</h3>
            {groups.filter(group => group.nodeIds.includes(node.id) && group.kind !== 'Boundary').map(group =>
              <p key={group.id}>{group.label} ({group.kind})</p>)}
            {!groups.some(group => group.nodeIds.includes(node.id) && group.kind !== 'Boundary')
              && <p>No internal group association recorded. Manage recorded groups in System design; no system-area classification is inferred.</p>}
            <p>Internal groups, hosting environments and network zones do not establish authorization scope.</p>
          </section>
          <p className="sd-muted">A provider-hosted application may be included. Provider associations alone do not decide scope.
            Separately authorized is recorded information, not verified coverage without supporting evidence.</p>
          {field('Environment', 'environment')}
          <details className="col-span-full rounded-lg border border-slate-200 p-3 dark:border-slate-700">
            <summary>Advanced scope details</summary>
            <p className="sd-muted">Optional named definition association: select only an applicable recorded option.
              Physical, Logical and Hybrid definitions do not establish authorization scope or a system-area classification.
              An app and its API do not each require a new enclosing boundary. Missing associations remain review work.</p>
            <label className="sd-field">Named boundary scope
              <select value={draft.boundaryDefinitionId ?? ''} onChange={event => set('boundaryDefinitionId', event.target.value)}>
                <option value="">Not selected</option>
                {nodes.filter(item => item.kind === 'BoundaryDefinition' && item.source?.id).map(item =>
                  <option key={item.id} value={item.source!.id}>{item.label} ({item.properties.BoundaryType || 'Type not recorded'})</option>)}
                {draft.boundaryDefinitionId && !nodes.some(item => item.kind === 'BoundaryDefinition' && item.source?.id === draft.boundaryDefinitionId)
                  && <option value={draft.boundaryDefinitionId}>Recorded selection unavailable — reconcile source</option>}
              </select>
            </label>
            {field('Recorded scope relationship', 'boundaryRelationship', ['SystemManaged', 'SharedService', 'SeparatelyAuthorized', 'Undetermined'])}
            {field('Network / trust zone', 'networkZone')}{field('Provider', 'provider')}
            {field('External authorization/source reference URL', 'externalAuthorizationReference')}
            <p className="sd-muted">Design record: {node.id}. These annotations do not change canonical placement or an AO decision.</p>
            {node.source && <p className="sd-muted">Recorded type: {node.properties.SubType || node.properties.ComponentType || node.kind}
              {' · '}Source: {node.source.provenance} · {node.source.type} · {node.source.id} · Version {node.source.version}.
              Canonical type and ownership records are edited in their source workflow.
              {node.source.resolutionUrl.startsWith('/') && !node.source.resolutionUrl.startsWith('//')
                && !/[\\\u0000-\u0020]/.test(node.source.resolutionUrl)
                && <Link className="sd-button" to={node.source.resolutionUrl}>Open source record</Link>}</p>}
          </details>
          {!node.source && contextField('Component type', 'componentType')}
        </>}
        {!inventoryOnly && <>
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
        {!inventoryOnly && field('SSP impact', 'sspImpact')}
        </>}
        {!inventoryOnly && isArchitectureNode(node) && <>
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
        {!inventoryOnly && isNetworkComponent(node) && <>
          {field('Network component role', 'networkRole', NETWORK_ROLES)}
          {field('Network segment / enclave', 'networkSegment')}
          {field('Network IP / CIDR address', 'networkAddress')}
          {field('Claimed hosting impact level', 'hostingImpactLevel', ['IL2', 'IL3', 'IL4', 'IL5', 'IL6'])}
          <p className="sd-muted">Reuse canonical inventory addresses where recorded. These annotations do not establish IL accreditation, accepted inheritance, inventory completeness or authorization. Source and evidence remain reviewable separately.</p>
        </>}
        {!inventoryOnly && (isArchitectureNode(node) || node.kind === 'DataFlowElement'
          || node.kind === 'LogicalConstruct' && node.properties.logicalType === 'Activity') && <>
          {field('DFD role', 'dataFlowRole', node.kind === 'LogicalConstruct' ? ['Function', 'Undetermined'] : DFD_ROLES)}
          {field('System function / transformation description', 'functionDescription')}
          {field('Data retention', 'dataRetention')}
          {field('Data disposal / destruction method', 'disposalMethod')}
          <p className="sd-muted">These are reviewed design annotations, not canonical record changes. Record actual functions, stores and external data producers/consumers; hosting/CSP links do not establish a data exchange.</p>
        </>}
        {!inventoryOnly && (node.kind === 'LogicalConstruct' ? <>
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
        </>)}
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
