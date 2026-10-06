const labels: Record<string, string> = {
  MissingAllocation: 'Responsibility not confirmed',
  Undesignated: 'Responsibility not confirmed',
  PendingReview: 'Review responsibility',
  ConflictingAllocations: 'Resolve conflicting responsibilities',
  PreservedOverride: 'System override retained',
  Applied: 'Confirmed responsibility',
  Ready: 'Confirmed; application pending',
  Persisted: 'Recorded system responsibility',
  MissingBaseline: 'Select a system baseline',
  OutsideBaseline: 'Not in the selected baseline',
  Inactive: 'Source no longer active',
};

export function responsibilityLabel(state: string): string {
  return labels[state] ?? 'Review state unavailable';
}

export function responsibilityNeedsReview(state: string): boolean {
  return !['Applied', 'Ready', 'Persisted', 'Inactive', 'OutsideBaseline'].includes(state);
}
