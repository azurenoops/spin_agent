import type { PackageDecision } from './types';

export function packagePublicationKey(preview: PackageDecision): string {
  // A server preview ID binds one immutable selection, so refresh and other tabs replay the same intent.
  return `package-publication-${preview.previewId}`;
}
