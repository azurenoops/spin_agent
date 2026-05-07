import type { OnboardingStateDto, WizardStepName } from '../api/onboardingApi';

/**
 * Step metadata for the navigator (FR-004 / FR-005).
 */
export const WIZARD_STEPS: ReadonlyArray<{
  name: WizardStepName;
  number: number;
  title: string;
  skippable: boolean;
}> = [
  { name: 'OrganizationContext', number: 1, title: 'Organization Information', skippable: false },
  { name: 'Roles', number: 2, title: 'Assign RMF roles', skippable: false },
  { name: 'AzureSubscriptions', number: 3, title: 'Add Azure subscriptions', skippable: false },
  { name: 'Templates', number: 4, title: 'Import Document templates', skippable: true },
  { name: 'Emass', number: 5, title: 'Import eMASS package', skippable: true },
  { name: 'SspPdf', number: 6, title: 'SSP PDF ingestion', skippable: true },
  { name: 'NarrativeSeeds', number: 7, title: 'Define Narrative seeds', skippable: true },
];

interface Props {
  state: OnboardingStateDto | null;
  currentStep: WizardStepName;
  onNavigate: (step: WizardStepName) => void;
}

/**
 * `WizardStepNavigator` — visualizes step progress with deep-linkable nav targets.
 * Foundational shell; downstream stories add inline status badges + dependency
 * indicators per step.
 */
export default function WizardStepNavigator({ state, currentStep, onNavigate }: Props) {
  const completed = new Set(state?.steps.map(s => s.step) ?? []);

  return (
    <nav aria-label="Onboarding steps" className="flex flex-col gap-1 p-3 border-r border-gray-200 min-w-[260px]">
      {WIZARD_STEPS.map(step => {
        const isCurrent = step.name === currentStep;
        const isDone = completed.has(step.name);
        return (
          <button
            key={step.name}
            type="button"
            onClick={() => onNavigate(step.name)}
            className={[
              'text-left px-3 py-2 rounded transition-colors flex items-center gap-3',
              isCurrent ? 'bg-indigo-100 text-indigo-900 font-semibold' : 'hover:bg-gray-100',
            ].join(' ')}
          >
            <span
              aria-hidden="true"
              className={[
                'inline-flex items-center justify-center w-6 h-6 rounded-full text-xs font-bold',
                isDone
                  ? 'bg-green-600 text-white'
                  : isCurrent
                    ? 'bg-indigo-600 text-white'
                    : 'bg-gray-200 text-gray-700',
              ].join(' ')}
            >
              {isDone ? '✓' : step.number}
            </span>
            <span className="flex-1">
              <span className="block">{step.title}</span>
              {step.skippable && (
                <span className="text-xs text-gray-500">Skippable</span>
              )}
            </span>
          </button>
        );
      })}
    </nav>
  );
}
