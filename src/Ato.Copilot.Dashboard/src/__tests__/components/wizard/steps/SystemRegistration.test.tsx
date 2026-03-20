import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import SystemRegistration from '../../../../components/wizard/steps/SystemRegistration';

// Mock the API
vi.mock('../../../../api/portfolio', () => ({
  registerSystem: vi.fn(),
  generateSystemDescription: vi.fn(),
}));

import { registerSystem } from '../../../../api/portfolio';

const mockRegisterSystem = registerSystem as ReturnType<typeof vi.fn>;

const defaultData = {
  name: '',
  acronym: '',
  systemType: 'MajorApplication',
  missionCriticality: 'MissionEssential',
  hostingEnvironment: 'AzureGovernment',
  description: '',
};

describe('SystemRegistration', () => {
  const defaultProps = {
    data: { ...defaultData },
    errors: {} as Record<string, string[]>,
    onNext: vi.fn(),
    onSystemId: vi.fn(),
    onErrors: vi.fn(),
    onClearErrors: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders all required form fields', () => {
    render(<SystemRegistration {...defaultProps} />);

    expect(screen.getByText(/system name/i)).toBeDefined();
    expect(screen.getByText(/system type/i)).toBeDefined();
    expect(screen.getByText(/mission criticality/i)).toBeDefined();
    expect(screen.getByText(/hosting environment/i)).toBeDefined();
  });

  it('disables Next button when name is empty', () => {
    render(<SystemRegistration {...defaultProps} />);

    const nextBtn = screen.getByRole('button', { name: /next/i });
    expect(nextBtn.hasAttribute('disabled')).toBe(true);
  });

  it('shows validation error for name exceeding max length', async () => {
    const longName = 'A'.repeat(201);
    render(
      <SystemRegistration
        {...defaultProps}
        data={{ ...defaultData, name: longName }}
      />,
    );

    const nextBtn = screen.getByRole('button', { name: /next/i });
    fireEvent.click(nextBtn);

    await waitFor(() => {
      expect(defaultProps.onErrors).toHaveBeenCalledWith(
        expect.objectContaining({ name: expect.arrayContaining([expect.stringContaining('200')]) }),
      );
    });
    expect(defaultProps.onNext).not.toHaveBeenCalled();
  });

  it('calls registerSystem and onNext on successful submission', async () => {
    mockRegisterSystem.mockResolvedValueOnce({
      id: 'sys-new',
      name: 'Test System',
    });

    render(
      <SystemRegistration
        {...defaultProps}
        data={{ ...defaultData, name: 'Test System' }}
      />,
    );

    const nextBtn = screen.getByRole('button', { name: /next/i });
    fireEvent.click(nextBtn);

    await waitFor(() => {
      expect(mockRegisterSystem).toHaveBeenCalled();
    });
  });

  it('displays duplicate name error from API', async () => {
    const duplError = new Error('Duplicate') as Error & { errorCode: string };
    duplError.errorCode = 'DUPLICATE_NAME';
    mockRegisterSystem.mockRejectedValueOnce(duplError);

    render(
      <SystemRegistration
        {...defaultProps}
        data={{ ...defaultData, name: 'Duplicate System' }}
      />,
    );

    const nextBtn = screen.getByRole('button', { name: /next/i });
    fireEvent.click(nextBtn);

    await waitFor(() => {
      expect(defaultProps.onErrors).toHaveBeenCalled();
    });
  });

  it('renders form-level errors from props', () => {
    render(
      <SystemRegistration
        {...defaultProps}
        errors={{ _form: ['Something went wrong'] }}
      />,
    );

    expect(screen.getByText('Something went wrong')).toBeDefined();
  });
});
