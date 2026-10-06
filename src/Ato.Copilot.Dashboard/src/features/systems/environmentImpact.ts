import type { EnvironmentImpactPreview } from '../../api/systemEnvironments';

export function hasImpactBlockers(preview: EnvironmentImpactPreview) {
  return preview.canCommit === false || !!preview.blockers?.length;
}

export function isCurrentImpactPreview(preview: EnvironmentImpactPreview) {
  const expiresAt = Date.parse(preview.expiresAt);
  return Number.isFinite(expiresAt) && expiresAt > Date.now();
}
