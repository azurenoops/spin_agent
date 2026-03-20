import { useReducer, useCallback } from 'react';
import { WizardStep } from '../types/dashboard';
import type { WizardState, WizardStepData } from '../types/dashboard';

// ─── Actions ───────────────────────────────────────────────────────────────────

type WizardAction =
  | { type: 'OPEN' }
  | { type: 'CANCEL' }
  | { type: 'RESET' }
  | { type: 'NEXT_STEP'; data?: Partial<WizardStepData> }
  | { type: 'PREV_STEP' }
  | { type: 'SKIP_STEP' }
  | { type: 'GO_TO_STEP'; step: WizardStep }
  | { type: 'SET_SYSTEM_ID'; systemId: string }
  | { type: 'SET_VALIDATION_ERRORS'; errors: Record<string, string[]> }
  | { type: 'CLEAR_VALIDATION_ERRORS' }
  | { type: 'FINISH' };

// ─── Initial state ─────────────────────────────────────────────────────────────

const initialStepData: WizardStepData = {
  registration: {
    name: '',
    acronym: '',
    systemType: 'MajorApplication',
    missionCriticality: 'MissionEssential',
    hostingEnvironment: 'AzureGovernment',
    description: '',
  },
  capabilities: { capabilityIds: [] },
  components: { componentIds: [] },
  boundaries: { boundaryIds: [] },
  roles: { roleAssignments: [] },
  verifyRoles: {},
  privacy: {
    collectsPii: false,
    maintainsPii: false,
    disseminatesPii: false,
    piiCategories: [],
    estimatedRecordCount: null,
    purpose: '',
  },
  categorization: {
    informationTypes: [],
    isNationalSecuritySystem: false,
    justification: '',
  },
};

const initialState: WizardState = {
  currentStep: WizardStep.Registration,
  systemId: null,
  stepData: { ...initialStepData },
  validationErrors: {},
  completedSteps: [false, false, false, false, false, false, false, false],
  isOpen: false,
};

// ─── Reducer ───────────────────────────────────────────────────────────────────

function wizardReducer(state: WizardState, action: WizardAction): WizardState {
  switch (action.type) {
    case 'OPEN':
      return { ...initialState, isOpen: true };

    case 'CANCEL':
      return { ...initialState, isOpen: false };

    case 'RESET':
      return { ...initialState };

    case 'NEXT_STEP': {
      if (state.currentStep >= WizardStep.PrivacyAnalysis) return state;
      const stepIndex = state.currentStep - 1;
      const newCompleted = [...state.completedSteps];
      newCompleted[stepIndex] = true;
      const merged = action.data
        ? { ...state.stepData, ...action.data }
        : state.stepData;
      return {
        ...state,
        currentStep: (state.currentStep + 1) as WizardStep,
        stepData: merged,
        completedSteps: newCompleted,
        validationErrors: {},
      };
    }

    case 'PREV_STEP': {
      if (state.currentStep <= WizardStep.Registration) return state;
      return {
        ...state,
        currentStep: (state.currentStep - 1) as WizardStep,
        validationErrors: {},
      };
    }

    case 'SKIP_STEP': {
      if (state.currentStep <= WizardStep.Registration) return state;
      if (state.currentStep >= WizardStep.PrivacyAnalysis) return state;
      const stepIndex = state.currentStep - 1;
      const newCompleted = [...state.completedSteps];
      newCompleted[stepIndex] = true;
      return {
        ...state,
        currentStep: (state.currentStep + 1) as WizardStep,
        completedSteps: newCompleted,
        validationErrors: {},
      };
    }

    case 'GO_TO_STEP': {
      // Only navigate backward to completed steps
      if (action.step >= state.currentStep) return state;
      if (!state.completedSteps[action.step - 1] && action.step !== state.currentStep) return state;
      return {
        ...state,
        currentStep: action.step,
        validationErrors: {},
      };
    }

    case 'SET_SYSTEM_ID':
      return { ...state, systemId: action.systemId };

    case 'SET_VALIDATION_ERRORS':
      return { ...state, validationErrors: action.errors };

    case 'CLEAR_VALIDATION_ERRORS':
      return { ...state, validationErrors: {} };

    case 'FINISH': {
      const newCompleted = [...state.completedSteps];
      newCompleted[state.currentStep - 1] = true;
      return {
        ...state,
        completedSteps: newCompleted,
        validationErrors: {},
      };
    }

    default:
      return state;
  }
}

// ─── Hook ──────────────────────────────────────────────────────────────────────

export function useIntakeWizard() {
  const [state, dispatch] = useReducer(wizardReducer, initialState);

  const open = useCallback(() => dispatch({ type: 'OPEN' }), []);
  const cancel = useCallback(() => dispatch({ type: 'CANCEL' }), []);
  const reset = useCallback(() => dispatch({ type: 'RESET' }), []);

  const nextStep = useCallback(
    (data?: Partial<WizardStepData>) => dispatch({ type: 'NEXT_STEP', data }),
    [],
  );
  const prevStep = useCallback(() => dispatch({ type: 'PREV_STEP' }), []);
  const skipStep = useCallback(() => dispatch({ type: 'SKIP_STEP' }), []);

  const goToStep = useCallback(
    (step: WizardStep) => dispatch({ type: 'GO_TO_STEP', step }),
    [],
  );

  const setSystemId = useCallback(
    (systemId: string) => dispatch({ type: 'SET_SYSTEM_ID', systemId }),
    [],
  );

  const setValidationErrors = useCallback(
    (errors: Record<string, string[]>) =>
      dispatch({ type: 'SET_VALIDATION_ERRORS', errors }),
    [],
  );

  const clearValidationErrors = useCallback(
    () => dispatch({ type: 'CLEAR_VALIDATION_ERRORS' }),
    [],
  );

  const finish = useCallback(() => dispatch({ type: 'FINISH' }), []);

  return {
    state,
    open,
    cancel,
    reset,
    nextStep,
    prevStep,
    skipStep,
    goToStep,
    setSystemId,
    setValidationErrors,
    clearValidationErrors,
    finish,
  };
}
