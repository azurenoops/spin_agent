import { getPackageCandidates } from '../package-imports/api';
import { getAssociatedPackage, getOffering } from '../provider-authorizations/api';
import { readAllPages } from '../provider-authorizations/providerReadModels';
import type { ProviderSourceArtifact } from './types';

export async function readProviderCapabilitySources(capabilityId: string, artifacts: ProviderSourceArtifact[], signal: AbortSignal) {
  const packageIds = [...new Set(artifacts.flatMap(item => {
    const match = /^package:([0-9a-f-]{36})\/artifact:[0-9a-f-]{36}$/i.exec(item.sourceReference ?? '');
    return match?.[1] ? [match[1]] : [];
  }))];
  const sources = await Promise.all(packageIds.map(async packageId => {
    const receipt = await getAssociatedPackage(packageId, signal);
    if (receipt.packageId !== packageId) throw new Error('Source package response does not match this capability reference.');
    const candidates = await readAllPages(page => getPackageCandidates(packageId,
      { page, pageSize: 100, type: 'Responsibility' }, signal), signal);
    return { packageId, offeringId: receipt.association?.offeringId,
      duties: candidates.filter(item => item.type === 'Responsibility'
        && item.contributorIds.some(id => id.toLowerCase() === capabilityId.toLowerCase())) };
  }));
  const offeringIds = [...new Set(sources.flatMap(item => item.offeringId ? [item.offeringId] : []))];
  const offeringId = offeringIds.length === 1 ? offeringIds[0] : undefined;
  const offering = offeringId ? await getOffering(offeringId, signal) : null;
  return { offering, sources };
}
