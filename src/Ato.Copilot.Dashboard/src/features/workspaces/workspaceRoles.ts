const labels = new Map([
  ['issm', 'ISSM'],
  ['isso', 'ISSO'],
  ['sca', 'SCA'],
  ['authorizingofficial', 'AO'],
  ['ao', 'AO'],
  ['missionowner', 'MissionOwner'],
  ['systemowner', 'SystemOwner'],
  ['engineer', 'Engineer'],
  ['administrator', 'Administrator'],
]);

/** Display/focus aliases only; operation authorization uses server permissions. */
export function displayWorkspaceRoles(roles: readonly string[] | undefined, legacyRole = ''): string[] {
  return [...new Set((roles ?? (legacyRole ? [legacyRole] : []))
    .filter(Boolean).map(role => labels.get(role.toLowerCase()) ?? role))];
}
