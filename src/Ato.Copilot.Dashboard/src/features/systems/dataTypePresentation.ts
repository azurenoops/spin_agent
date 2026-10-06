export interface DataDocumentationRow {
  id?: string; _tempId?: string; dataTypeName?: string; description?: string | null; sensitivityClassification?: string;
  cuiCategory?: string | null; confidentialityImpact?: string | null; integrityImpact?: string | null; availabilityImpact?: string | null;
  privacyApplicability?: string | null; retentionRule?: string | null; disposalMethod?: string | null; categorizationReference?: string | null;
}
const fields = ['dataTypeName', 'description', 'sensitivityClassification', 'confidentialityImpact', 'integrityImpact',
  'availabilityImpact', 'privacyApplicability', 'retentionRule', 'disposalMethod', 'categorizationReference'] as const;
const recorded = (value?: string | null) => !!value?.trim() && !['undetermined', 'reviewrequired'].includes(value.trim().toLowerCase());
export const isCuiDeclared = (row: DataDocumentationRow) => row.sensitivityClassification?.trim().toUpperCase().startsWith('CUI') === true;
export function dataDocumentation(rows: DataDocumentationRow[]) {
  let count = 0;
  let total = 0;
  let firstIncompleteId: string | undefined;
  let firstIncompleteName: string | undefined;
  for (const row of rows) {
    const applicable = [...fields, ...(isCuiDeclared(row) ? ['cuiCategory' as const] : [])];
    const complete = applicable.filter(field => recorded(row[field])).length;
    count += complete; total += applicable.length;
    if (complete < applicable.length && firstIncompleteName === undefined) {
      firstIncompleteId = row.id ?? row._tempId;
      firstIncompleteName = row.dataTypeName || 'Unnamed information type';
    }
  }
  return { recorded: count, total, missing: total - count, types: rows.length, firstIncompleteId, firstIncompleteName };
}
export type DataDocumentation = ReturnType<typeof dataDocumentation>;
export function ciaSummary(row: DataDocumentationRow): string {
  const impacts: Record<string, string> = { Low: 'L', Moderate: 'M', High: 'H' };
  return [row.confidentialityImpact, row.integrityImpact, row.availabilityImpact].map(value => impacts[value ?? ''] ?? '?').join(' / ') + ' · declared';
}
export function privacyLabel(value: string): string {
  const labels: Record<string, string> = { NoPii: 'No PII declared', PiiApplies: 'PII applies · declared',
    ReviewRequired: 'Privacy review required', Undetermined: 'Privacy undetermined' };
  return labels[value] ?? value;
}
export function privacySummary(row: DataDocumentationRow): string {
  return `${row.privacyApplicability ? privacyLabel(row.privacyApplicability) : 'Privacy not recorded'} · ${row.retentionRule || 'Retention not recorded'}`;
}
