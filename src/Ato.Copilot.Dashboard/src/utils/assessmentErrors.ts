import { isAxiosError } from 'axios';

export interface AssessmentError {
  message: string;
  suggestion: string | null;
  errorCode: string | null;
}

export function assessmentError(error: unknown, fallback: string): AssessmentError {
  if (error && typeof error === 'object' && 'error' in error && typeof error.error === 'string') {
    return {
      message: error.error || fallback,
      suggestion: 'suggestion' in error && typeof error.suggestion === 'string' ? error.suggestion : null,
      errorCode: 'errorCode' in error && typeof error.errorCode === 'string' ? error.errorCode : null,
    };
  }
  if (isAxiosError(error) && error.response?.status === 403) {
    return {
      message: 'You do not have permission to configure or run Azure assessments.',
      suggestion: 'Ask an authorized compliance writer to configure or run the assessment.',
      errorCode: 'ASSESSMENT_PERMISSION_REQUIRED',
    };
  }
  return { message: error instanceof Error && error.message ? error.message : fallback, suggestion: null, errorCode: null };
}
