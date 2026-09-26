export function assessmentConfigurationUrl(systemId: string): string {
  return `/systems/${encodeURIComponent(systemId)}/assessments/environment#azure-assessment-environment`;
}
