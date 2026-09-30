import type { EnvironmentDiscoveryResponse, EnvironmentExcludedResource } from '../../api/systemEnvironments';

export interface ResourceSelection {
  resourceIds: string[];
  exclusions: EnvironmentExcludedResource[];
  sharedDependencyResourceIds: string[];
}

export default function EnvironmentResourceSelection({ discovery, value, disabled, onChange }: {
  discovery: EnvironmentDiscoveryResponse; value: ResourceSelection; disabled: boolean;
  onChange: (value: ResourceSelection) => void;
}) {
  const groups = [...new Set(discovery.resources.map(resource => resource.resourceGroup))];
  const toggle = (ids: string[], included: boolean) => onChange({
    ...value,
    resourceIds: included ? [...new Set([...value.resourceIds, ...ids])] : value.resourceIds.filter(id => !ids.includes(id)),
    exclusions: included ? value.exclusions.filter(item => !ids.includes(item.resourceId)) : value.exclusions,
  });
  return <div className="space-y-4">
    <p className="text-xs text-slate-500">Select current resources only. Future resources are not automatically included, even in a selected group. Other resources remain outside this system scope.</p>
    {!discovery.resources.length && <p>No resources were discovered.</p>}
    {groups.map(group => {
      const resources = discovery.resources.filter(resource => resource.resourceGroup === group);
      return <fieldset key={group} disabled={disabled} className="rounded-lg border border-slate-200 p-3">
        <legend className="px-2 text-sm font-semibold"><label><input type="checkbox" disabled={disabled}
          checked={resources.every(item => value.resourceIds.includes(item.resourceId))}
          onChange={event => toggle(resources.map(item => item.resourceId), event.target.checked)} /> {group} · {resources.length} resources</label></legend>
        {resources.map(item => <div key={item.resourceId} className="border-b py-3 text-sm last:border-0">
          <label><input aria-label={`Include ${item.name}`} type="checkbox" checked={value.resourceIds.includes(item.resourceId)}
            onChange={event => toggle([item.resourceId], event.target.checked)} /> <strong>{item.name}</strong> <span className="text-xs text-slate-500">{item.resourceType}</span></label>
          <details className="mt-2 text-xs"><summary>Scope details</summary><p className="break-all">{item.resourceId}</p>
            <label className="mt-2 block"><input type="checkbox" checked={value.sharedDependencyResourceIds.includes(item.resourceId)}
              onChange={event => onChange({ ...value, sharedDependencyResourceIds: event.target.checked
                ? [...new Set([...value.sharedDependencyResourceIds, item.resourceId])]
                : value.sharedDependencyResourceIds.filter(id => id !== item.resourceId) })} /> Shared dependency</label>
            {!value.resourceIds.includes(item.resourceId) && <label className="mt-2 block">Exclusion rationale (optional explicit record)
              <input className="mt-1 w-full rounded border p-2 dark:bg-slate-900" maxLength={2000}
                value={value.exclusions.find(excluded => excluded.resourceId === item.resourceId)?.rationale ?? ''}
                onChange={event => onChange({ ...value, exclusions: [...value.exclusions.filter(excluded => excluded.resourceId !== item.resourceId),
                  ...(event.target.value.trim() ? [{ resourceId: item.resourceId, rationale: event.target.value }] : [])] })} /></label>}
          </details>
        </div>)}
      </fieldset>;
    })}
  </div>;
}
