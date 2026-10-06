import type { ResponsibilityDraftContext, ResponsibilityValue } from '../../src/api/responsibilityDrafts';

const value = (text = ''): ResponsibilityValue => ({
  value: text, origin: 'From system records', sourceIds: [], explanation: '', userEdited: false, sourceHash: 'source-1',
});
export function appliedResponsibilityContext(systemId: string, controlId: string, canPrepare = true): ResponsibilityDraftContext {
  return { systemId, controlId, baselineId: 'baseline-a', scopeId: null, canPrepare, sourceHash: 'source-1',
    scopes: [], sources: [], questions: ['Verify recorded system applicability'], conflicts: [], draft: null,
    sourceValues: { allocation: value('Customer'), provider: value(), providerDuties: value(),
      customer: value('Recorded fixture duty'), scope: value(), exclusions: value(), source: value(),
      basis: value('Recorded source basis'), information: value() },
  };
}
