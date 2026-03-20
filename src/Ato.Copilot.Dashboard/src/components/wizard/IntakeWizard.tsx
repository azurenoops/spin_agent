import { WizardStep } from '../../types/dashboard';
import type { WizardState, WizardStepData } from '../../types/dashboard';
import WizardStepper from './WizardStepper';
import CompletionSummary from './steps/CompletionSummary';
import SystemRegistration from './steps/SystemRegistration';
import SecurityCapabilities from './steps/SecurityCapabilities';
import SystemComponents from './steps/SystemComponents';
import AuthorizationBoundaries from './steps/AuthorizationBoundaries';
import AssignRoles from './steps/AssignRoles';
import VerifyRoles from './steps/VerifyRoles';
import PrivacyAnalysis from './steps/PrivacyAnalysis';
import SetCategorization from './steps/SetCategorization';

interface IntakeWizardProps {
  state: WizardState;
  onNext: (data?: Partial<WizardStepData>) => void;
  onPrev: () => void;
  onSkip: () => void;
  onGoToStep: (step: WizardStep) => void;
  onCancel: () => void;
  onFinish: () => void;
  onSystemId: (id: string) => void;
  onValidationErrors: (errors: Record<string, string[]>) => void;
  onClearErrors: () => void;
}

export default function IntakeWizard({
  state,
  onNext,
  onPrev,
  onSkip,
  onGoToStep,
  onCancel,
  onFinish,
  onSystemId,
  onValidationErrors,
  onClearErrors,
}: IntakeWizardProps) {
  const { currentStep, systemId, stepData, validationErrors, completedSteps } = state;

  // Show completion screen after final step
  const showCompletion = currentStep === WizardStep.PrivacyAnalysis && completedSteps[7];

  const canSkip = currentStep > WizardStep.Registration && currentStep < WizardStep.PrivacyAnalysis;
  const canGoBack = currentStep > WizardStep.Registration;

  const renderCurrentStep = () => {
    if (showCompletion) {
      return (
        <CompletionSummary
          systemId={systemId!}
          systemName={stepData.registration.name}
          completedSteps={completedSteps}
          onClose={onCancel}
        />
      );
    }

    switch (currentStep) {
      case WizardStep.Registration:
        return (
          <SystemRegistration
            data={stepData.registration}
            errors={validationErrors}
            onNext={(data) => onNext({ registration: data } as Partial<WizardStepData>)}
            onSystemId={onSystemId}
            onErrors={onValidationErrors}
            onClearErrors={onClearErrors}
          />
        );
      case WizardStep.SecurityCapabilities:
        return (
          <SecurityCapabilities
            systemId={systemId!}
            onNext={() => onNext()}
            onErrors={onValidationErrors}
          />
        );
      case WizardStep.SystemComponents:
        return (
          <SystemComponents
            systemId={systemId!}
            onNext={() => onNext()}
            onErrors={onValidationErrors}
          />
        );
      case WizardStep.AuthorizationBoundaries:
        return (
          <AuthorizationBoundaries
            systemId={systemId!}
            onNext={() => onNext()}
            onErrors={onValidationErrors}
          />
        );
      case WizardStep.AssignRoles:
        return (
          <AssignRoles
            systemId={systemId!}
            onNext={() => onNext()}
            onErrors={onValidationErrors}
          />
        );
      case WizardStep.VerifyRoles:
        return (
          <VerifyRoles
            systemId={systemId!}
            onNext={() => onNext()}
          />
        );
      case WizardStep.Categorization:
        return (
          <SetCategorization
            systemId={systemId!}
            onNext={() => onNext()}
            onErrors={onValidationErrors}
          />
        );
      case WizardStep.PrivacyAnalysis:
        return (
          <PrivacyAnalysis
            systemId={systemId!}
            onFinish={onFinish}
            onBack={onPrev}
            onCancel={onCancel}
            onErrors={onValidationErrors}
          />
        );
      default:
        return null;
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex flex-col bg-white">
      {/* Header */}
      <div className="flex items-center justify-between px-6 py-3 border-b border-gray-200">
        <h1 className="text-lg font-semibold text-gray-900">Register New System</h1>
        <button
          onClick={onCancel}
          className="rounded-md p-1.5 text-gray-500 hover:text-gray-700 hover:bg-gray-100"
          aria-label="Close wizard"
        >
          <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>

      {/* Stepper */}
      {!showCompletion && (
        <WizardStepper
          currentStep={currentStep}
          completedSteps={completedSteps}
          onStepClick={onGoToStep}
        />
      )}

      {/* Step content */}
      <div className="flex-1 overflow-y-auto">
        <div className="mx-auto max-w-4xl px-6 py-8">
          {renderCurrentStep()}
        </div>
      </div>

      {/* Footer buttons — hidden during completion and PrivacyAnalysis (which has its own Finish row) */}
      {!showCompletion && currentStep !== WizardStep.Registration && currentStep !== WizardStep.PrivacyAnalysis && (
        <div className="flex items-center justify-between border-t border-gray-200 px-6 py-4 bg-white">
          <div>
            {canGoBack && (
              <button
                onClick={onPrev}
                className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
              >
                Back
              </button>
            )}
          </div>
          <div className="flex gap-3">
            {canSkip && (
              <button
                onClick={onSkip}
                className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
              >
                Skip
              </button>
            )}
            <button
              onClick={onCancel}
              className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
            >
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
