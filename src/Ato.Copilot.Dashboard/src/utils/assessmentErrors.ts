import { isAxiosError } from 'axios';

export interface AssessmentError {
  message: string;
  suggestion: string | null;
  errorCode: string | null;
}

export const assessmentPermissionError: AssessmentError = {
  message: 'You do not have permission to configure or run Azure assessments for this system.',
  suggestion: 'Ask an authorized compliance writer with assessment access to this system to configure or run the assessment. Access is determined by your signed-in account and system permissions, not the browser persona.',
  errorCode: 'ASSESSMENT_PERMISSION_REQUIRED',
};

const assessmentAuthenticationError: AssessmentError = {
  message: 'Sign-in is required to configure or run Azure assessments.',
  suggestion: 'Sign in with an account that has assessment access to this system. Ask an authorized compliance writer if you need access.',
  errorCode: 'ASSESSMENT_AUTHENTICATION_REQUIRED',
};

export function isAssessmentAccessError(error: Pick<AssessmentError, 'errorCode'> | null | undefined): boolean {
  return ['FORBIDDEN', 'UNAUTHORIZED', 'ASSESSMENT_PERMISSION_REQUIRED', 'ASSESSMENT_AUTHENTICATION_REQUIRED']
    .includes(error?.errorCode ?? '');
}

export function assessmentError(error: unknown, fallback: string): AssessmentError {
  if (isAxiosError(error) && error.response?.status === 401) {
    return assessmentAuthenticationError;
  }
  if (isAxiosError(error) && error.response?.status === 403) {
    return assessmentPermissionError;
  }
  if (error && typeof error === 'object' && 'error' in error && typeof error.error === 'string') {
    const errorCode = 'errorCode' in error && typeof error.errorCode === 'string' ? error.errorCode : null;
    return {
      message: error.error || fallback,
      suggestion: 'suggestion' in error && typeof error.suggestion === 'string' ? error.suggestion
        : errorCode === 'UNAUTHORIZED' || errorCode === 'ASSESSMENT_AUTHENTICATION_REQUIRED' ? assessmentAuthenticationError.suggestion
          : isAssessmentAccessError({ errorCode }) ? assessmentPermissionError.suggestion : null,
      errorCode,
    };
  }
  return { message: error instanceof Error && error.message ? error.message : fallback, suggestion: null, errorCode: null };
}
