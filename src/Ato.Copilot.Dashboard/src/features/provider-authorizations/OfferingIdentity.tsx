import { useState } from 'react';
import { inputClass, message } from '../workspace-operations/workspaceUi';
import { Field, MutationForm } from './forms';
import { ProviderPanel } from './ProviderPresentation';
import * as api from './api';
import type { ManagementArrangement, Offering, ServiceModel } from './types';

export const serviceModels: Record<ServiceModel, string> = {
  InfrastructureSharedServices: 'Infrastructure + shared services', PlatformService: 'Platform service',
  SoftwareAsAService: 'Software as a service', BrokeredCloudSpace: 'Brokered cloud space',
};
export const managementArrangements: Record<ManagementArrangement, string> = {
  ProviderManaged: 'Provider managed', SharedOperations: 'Shared operations', MissionOwnerManaged: 'Mission Owner managed',
};
export interface IdentityMetadata {
  serviceModel: ServiceModel | ''; managementArrangement: ManagementArrangement | '';
  serviceOwner: string; securityContact: string;
}
export const identityMetadata = (offering?: Offering): IdentityMetadata => ({
  serviceModel: offering?.serviceModel ?? '', managementArrangement: offering?.managementArrangement ?? '',
  serviceOwner: offering?.serviceOwner ?? '', securityContact: offering?.securityContact ?? '',
});

export function OfferingIdentityFields({ value, onChange }: { value: IdentityMetadata; onChange: (value: IdentityMetadata) => void }) {
  return <div className="grid gap-4 sm:grid-cols-2">
    <label className="grid gap-1 text-sm">Service model<select className={inputClass} value={value.serviceModel}
      onChange={event => onChange({ ...value, serviceModel: event.target.value as IdentityMetadata['serviceModel'] })}>
      <option value="">Not recorded</option>{Object.entries(serviceModels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
    </select></label>
    <label className="grid gap-1 text-sm">Management arrangement<select className={inputClass} value={value.managementArrangement}
      onChange={event => onChange({ ...value, managementArrangement: event.target.value as IdentityMetadata['managementArrangement'] })}>
      <option value="">Not recorded</option>{Object.entries(managementArrangements).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
    </select></label>
    <Field label="Service owner" value={value.serviceOwner} maxLength={256} onChange={serviceOwner => onChange({ ...value, serviceOwner })} />
    <Field label="Security contact" value={value.securityContact} maxLength={256} onChange={securityContact => onChange({ ...value, securityContact })} />
  </div>;
}

export function OfferingIdentityEditor({ offering, onSaved, onPendingChange }: { offering: Offering; onSaved: (updated: Offering) => void; onPendingChange?: (pending: boolean) => void }) {
  const [name, setName] = useState(offering.name);
  const [description, setDescription] = useState(offering.description);
  const [metadata, setMetadata] = useState(() => identityMetadata(offering));
  const [base, setBase] = useState(offering);
  const [revision, setRevision] = useState(offering.revision);
  const [pending, setPending] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [refreshed, setRefreshed] = useState<Offering | null>(null);
  const [error, setError] = useState('');
  const refresh = async () => {
    setRefreshing(true); setError('');
    try {
      const current = await api.getOffering(offering.offeringId);
      if (current.offeringId !== offering.offeringId || !Number.isSafeInteger(current.revision) || current.revision < 1)
        throw new Error('The service identity did not match the selected offering and revision.');
      setRefreshed(current);
    }
    catch (reason) { setError(message(reason)); }
    finally { setRefreshing(false); }
  };
  return <div className="space-y-4">
    <MutationForm label="Save service identity" onPendingChange={value => { setPending(value); onPendingChange?.(value); }} disabled={refreshing} submitDisabled={!name.trim()}
      submit={async () => onSaved(await api.updateOffering(offering.offeringId, { name: name.trim(), description, environments: base.environments,
        ...metadata, expectedRevision: revision }))} onSaved={() => undefined}>
      <Field label="Offering name" value={name} maxLength={256} required onChange={setName} />
      <Field label="Description" value={description} multiline maxLength={8000} onChange={setDescription} />
      <OfferingIdentityFields value={metadata} onChange={setMetadata} />
      <p className="text-xs">Updating revision {revision} records service identity, not authorization or a live connector. Previously retained release contexts are not rewritten.</p>
    </MutationForm>
    <button className="provider-secondary" disabled={pending || refreshing} onClick={() => void refresh()}>Reload current service identity</button>
    {error && <p role="alert">{error}</p>}
    {refreshed && <ProviderPanel title={`Current service identity · revision ${refreshed.revision}`}>
      <dl className="grid gap-2 text-sm">
        <dt>Name</dt><dd>{refreshed.name}</dd><dt>Description</dt><dd>{refreshed.description}</dd>
        <dt>Environments</dt><dd>{refreshed.environments.join(', ')}</dd>
        <dt>Service model</dt><dd>{refreshed.serviceModel ? serviceModels[refreshed.serviceModel] : 'Not recorded'}</dd>
        <dt>Management arrangement</dt><dd>{refreshed.managementArrangement ? managementArrangements[refreshed.managementArrangement] : 'Not recorded'}</dd>
        <dt>Owner</dt><dd>{refreshed.serviceOwner || 'Not recorded'}</dd><dt>Security contact</dt><dd>{refreshed.securityContact || 'Not recorded'}</dd>
      </dl>
      <p className="text-xs">Compare current values before applying your retained edits. Accepting this revision does not save them.</p>
      <button className="provider-secondary mt-3" disabled={pending} onClick={() => {
        const previous = identityMetadata(base);
        const current = identityMetadata(refreshed);
        setName(value => value === base.name ? refreshed.name : value);
        setDescription(value => value === base.description ? refreshed.description : value);
        setMetadata(value => ({
          serviceModel: value.serviceModel === previous.serviceModel ? current.serviceModel : value.serviceModel,
          managementArrangement: value.managementArrangement === previous.managementArrangement ? current.managementArrangement : value.managementArrangement,
          serviceOwner: value.serviceOwner === previous.serviceOwner ? current.serviceOwner : value.serviceOwner,
          securityContact: value.securityContact === previous.securityContact ? current.securityContact : value.securityContact,
        }));
        setBase(refreshed); setRevision(refreshed.revision); setRefreshed(null);
      }}>Use refreshed revision with these edits</button>
    </ProviderPanel>}
  </div>;
}
