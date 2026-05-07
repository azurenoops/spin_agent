import { WizardStep } from '../../types/dashboard';

const STEPS = [
  { step: WizardStep.Registration, label: 'System Registration' },
  { step: WizardStep.SecurityCapabilities, label: 'Security Capabilities' },
  { step: WizardStep.SystemComponents, label: 'System Components' },
  { step: WizardStep.AuthorizationBoundaries, label: 'Authorization Boundaries' },
  { step: WizardStep.AssignRoles, label: 'Assign RMF Roles' },
  { step: WizardStep.VerifyRoles, label: 'Verify Roles' },
  { step: WizardStep.Categorization, label: 'Categorization & Baseline' },
  { step: WizardStep.PrivacyAnalysis, label: 'Privacy Analysis' },
];

interface WizardStepperProps {
  currentStep: WizardStep;
  completedSteps: boolean[];
  onStepClick: (step: WizardStep) => void;
}

export default function WizardStepper({ currentStep, completedSteps, onStepClick }: WizardStepperProps) {
  return (
    <nav className="flex items-center justify-between px-6 py-4 border-b border-gray-200 bg-gray-50 overflow-x-auto">
      {STEPS.map(({ step, label }, index) => {
        const isCompleted = completedSteps[index];
        const isCurrent = step === currentStep;
        const isFuture = step > currentStep && !isCompleted;
        const isClickable = isCompleted && !isCurrent;

        return (
          <div key={step} className="flex items-center">
            {index > 0 && (
              <div
                className={`hidden sm:block w-8 h-0.5 mx-1 ${
                  isCompleted || isCurrent ? 'bg-indigo-400' : 'bg-gray-300'
                }`}
              />
            )}
            <button
              type="button"
              onClick={() => isClickable && onStepClick(step)}
              disabled={!isClickable}
              className={`flex items-center gap-2 px-3 py-1.5 rounded-md text-sm font-medium whitespace-nowrap transition-colors ${
                isCurrent
                  ? 'bg-indigo-100 text-indigo-800 ring-1 ring-indigo-300'
                  : isCompleted
                    ? 'text-green-700 hover:bg-green-50 cursor-pointer'
                    : 'text-gray-400 cursor-default'
              }`}
            >
              <span
                className={`flex h-6 w-6 items-center justify-center rounded-full text-xs font-bold ${
                  isCompleted
                    ? 'bg-green-100 text-green-700'
                    : isCurrent
                      ? 'bg-indigo-600 text-white'
                      : 'bg-gray-200 text-gray-500'
                }`}
              >
                {isCompleted ? (
                  <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                  </svg>
                ) : (
                  index + 1
                )}
              </span>
              <span className={isFuture ? 'text-gray-400' : undefined}>{label}</span>
            </button>
          </div>
        );
      })}
    </nav>
  );
}
