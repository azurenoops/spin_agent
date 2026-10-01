import { useState } from 'react';
import type { DesignEdge, DesignNode } from '../../api/systemDesign';
import SetupDialog from '../workspace-operations/SetupDialog';
import { isArchitectureNode } from './graphAdapter';

export default function DesignRecordEditor({ node, edge, nodes, edges = [], onApply, onClose }: {
  node?: DesignNode; edge?: DesignEdge; nodes: DesignNode[]; edges?: DesignEdge[];
  onApply: (record: DesignNode | DesignEdge) => void; onClose: () => void;
}) {
  const [draft, setDraft] = useState<DesignNode | DesignEdge>(() => structuredClone(node ?? edge!));
  const [error, setError] = useState('');
  const canonicalConnection = edge?.source?.type === 'SystemInterconnection';
  const required = new Set(['label', 'sourceNodeId', 'targetNodeId', 'boundaryDisposition', 'relationshipType', 'direction', 'boundaryCrossing', 'sspImpact']);
  const set = (key: string, value: string) => setDraft(current => ({ ...current, [key]: value || (required.has(key) ? '' : null) }));
  const field = (label: string, key: string, options?: string[]) => <label className="sd-field" key={key}>{label}
    {options ? <select required={required.has(key)} value={String((draft as unknown as Record<string, unknown>)[key] ?? '')} onChange={event => set(key, event.target.value)}>
      <option value="">Not recorded</option>{options.map(option => <option key={option}>{option}</option>)}
    </select> : <input required={key === 'label'} value={String((draft as unknown as Record<string, unknown>)[key] ?? '')} onChange={event => set(key, event.target.value)} />}
  </label>;
  return <SetupDialog title={node ? 'Edit design element' : 'Edit data flow'} busy={false} onClose={onClose}
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
        {field('Environment', 'environment')}{field('Network / trust zone', 'networkZone')}{field('Provider', 'provider')}
        {field('SSP impact', 'sspImpact')}
      </> : <>
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
            <option value="">Choose recorded element</option>{nodes.filter(isArchitectureNode).map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
          </select></label>)}
        {canonicalConnection ? <>
          <label className="sd-field">Relationship type<select disabled value={edge.relationshipType}><option value={edge.relationshipType}>{edge.relationshipType}</option></select></label>
          <label className="sd-field">Direction<select disabled value={edge.direction}><option value={edge.direction}>{edge.direction}</option></select></label>
        </> : <>
          {field('Relationship type', 'relationshipType', ['DataFlow', 'Access', 'Dependency', 'NetworkConnection'])}
          {field('Direction', 'direction', ['Outbound', 'Inbound', 'Bidirectional'])}
        </>}
        {field('Purpose', 'purpose')}{field('Information type', 'informationType')}{field('Classification', 'classification')}
        {field('Port', 'port')}{field('Protocol', 'protocol')}{field('Service', 'service')}{field('Protection mechanism', 'protection')}
        {field('Encryption state', 'encryptionState', ['Encrypted', 'Unencrypted', 'Unknown'])}
        {field('Boundary crossing', 'boundaryCrossing', ['Yes', 'No', 'Unknown'])}
        <label className="sd-field">Recorded interconnection
          <select disabled={canonicalConnection} value={(draft as DesignEdge).interconnectionId ?? ''} onChange={event => {
            const record = edges.find(item => item.interconnectionId === event.target.value);
            setDraft(current => ({ ...current, interconnectionId: record?.interconnectionId ?? null,
              agreementStatus: record?.agreementStatus ?? null }));
          }}>
            <option value="">No recorded interconnection selected</option>
            {[...new Map([...edges, ...(edge ? [edge] : [])].filter(item => item.interconnectionId && (canonicalConnection
              || item.sourceNodeId === (draft as DesignEdge).sourceNodeId && item.targetNodeId === (draft as DesignEdge).targetNodeId
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
