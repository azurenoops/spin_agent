import { describe, expect, it } from 'vitest';
import { parameterPresentation, readableRequirement } from '../../features/narratives/requirementPresentation';

describe('Readable requirement wording', () => {
  it('handles OSCAL whitespace, multiple tokens, recorded values and unbound definitions without exposing code', () => {
    // Arrange
    const parameters = [{ id: 'p1', definition: '{"label":"authority"}' }, { id: 'p2', definition: '{"label":"processing"}' }];
    const text = 'Use {{insert: param, p1}} for {{ insert: param, p2 }}; {{ insert: param, unknown }}.';
    // Act
    const rendered = readableRequirement(text, parameters, { p1: 'Recorded authority' });
    // Assert
    expect(rendered).toBe('Use Recorded authority for [Processing — not recorded]; [Organization-defined value — source definition unavailable].');
  });
  it('flags legacy or unlabeled definitions rather than silently presenting an ID as a readable name', () => {
    // Arrange / Act
    const legacy = parameterPresentation({ id: 'legacy', definition: 'Legacy source' }, 0);
    const unlabeled = parameterPresentation({ id: 'p2', definition: '{"select":{"choice":["one","two"]}}' }, 1);
    // Assert
    expect(legacy.warning).toContain('unstructured');
    expect(unlabeled.name).toBe('Organization-defined value 2');
    expect(unlabeled.warning).toContain('readable parameter label');
  });
});
