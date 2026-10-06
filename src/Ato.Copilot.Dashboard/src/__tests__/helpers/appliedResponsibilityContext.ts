import type { ResponsibilityDraftContext, ResponsibilityValue } from '../../api/responsibilityDrafts';

const value = (text = ''): ResponsibilityValue => ({
  value: text, origin: 'From system records', sourceIds: [], explanation: '', userEdited: false, sourceHash: 'source-1',
});
export function appliedResponsibilityContext(systemId: string, controlId: string): ResponsibilityDraftContext {
  return { systemId, controlId, baselineId: 'baseline-a', scopeId: null, canPrepare: true, sourceHash: 'source-1',
    scopes: [], sources: [], questions: [], conflicts: [], draft: null,
    sourceValues: { allocation: value('Customer'), provider: value(), providerDuties: value(), customer: value('Recorded duty'),
      scope: value(), exclusions: value(), source: value(), basis: value('Recorded source basis'), information: value() },
  };
}
