import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import SystemDocumentPreview from '../../features/systems/SystemDocumentPreview';
import * as api from '../../api/exports';

vi.mock('../../api/exports', () => ({ getSspPreview: vi.fn(), retainSspPreview: vi.fn() }));
vi.mock('../../components/ExportSspDialog', () => ({
  default: ({ sourcePreviewId }: { sourcePreviewId: string }) => <div role="dialog">Export retained {sourcePreviewId}</div>,
}));
const preview = {
  systemId: 'system-a', format: 'json' as const, contentType: 'application/json' as const,
  content: JSON.stringify({ 'system-security-plan': {
    metadata: { title: 'Generated DEMO SSP' },
    'system-characteristics': { 'system-name': 'DEMO Mission', description: 'Exact generated mission description' },
  } }),
  contentHash: 'retained-content-hash', generatedAt: '2026-09-26T12:00:00Z',
  sourceGaps: [{ code: 'PROVIDER_SOURCE_MISSING', message: 'Reviewed issuer metadata is missing.' }],
  isPreview: true as const, sourceState: 'CurrentWorkingData' as const,
};
function mount(query = '') {
  render(<MemoryRouter initialEntries={[`/systems/system-a/documents/preview${query}`]}>
    <Routes><Route path="/systems/:id/documents/preview" element={<SystemDocumentPreview />} /></Routes>
  </MemoryRouter>);
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getSspPreview).mockResolvedValue(preview); });

describe('generated document preview', () => {
  it('organizes the SSP using the supplied template headings and marks missing source content', async () => {
    // Arrange
    const headings = ['1. Introduction', '2. Purpose', '3. System Information', '4. System Owner',
      '5. Assignment of Security Responsibility', '6. Leveraged FedRAMP-Authorized Services',
      '7. External Systems and Services Not Having FedRAMP Authorization',
      '8. Illustrated Architecture and Narratives', '9. Services, Ports, and Protocols',
      '10. Cryptographic Modules Implemented for Data At Rest (DAR) and Data In Transit (DIT)',
      '11. Separation of Duties', '12. SSP Appendices List'];
    // Act
    mount();
    await screen.findByRole('heading', { name: 'Generated DEMO SSP' });
    // Assert
    const template = screen.getByRole('region', { name: 'SSP template sections' });
    expect([...template.querySelectorAll('h3')].map(heading => heading.textContent)).toEqual(headings);
    expect(screen.getByRole('heading', { name: 'Prepared by' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Prepared for' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'SYSTEM SECURITY PLAN APPROVALS' })).toBeVisible();
    expect(screen.getByText('No approval signatures were supplied in the generated document.')).toBeVisible();
    expect(screen.getByText('No cryptographic module records were supplied in the generated document.')).toBeVisible();
    expect(screen.getByRole('navigation', { name: 'SSP document sections' })).toHaveTextContent('12. SSP Appendices List');
  });

  it('requests and retains approved-source mode without relabeling it as a working draft or authorization', async () => {
    // Arrange
    vi.mocked(api.getSspPreview).mockResolvedValue({ ...preview, sourceState: 'ApprovedSources', sourceGaps: [] });
    vi.mocked(api.retainSspPreview).mockResolvedValue({ ...preview, sourceState: 'ApprovedSources',
      previewId: 'approved-preview-a', sourceGaps: [] });
    // Act
    mount('?source=approved&contribution=SystemDesign');
    await screen.findByRole('heading', { name: 'Generated DEMO SSP' });
    fireEvent.click(screen.getByRole('button', { name: 'Retain generated preview' }));
    await screen.findByText(/server retained this exact generated document/);
    // Assert
    expect(api.getSspPreview).toHaveBeenCalledWith('system-a', expect.any(AbortSignal), 'approved');
    expect(api.retainSspPreview).toHaveBeenCalledWith('system-a', expect.any(String), expect.any(AbortSignal), 'approved');
    expect(screen.getByRole('region', { name: 'SSP cover page' })).toHaveTextContent('APPROVED SOURCE PREVIEW');
    expect(screen.getByRole('region', { name: 'SSP cover page' })).not.toHaveTextContent('WORKING DRAFT');
    expect(screen.getByRole('region', { name: 'SSP cover page' })).toHaveTextContent('does not establish authorization');
    expect(screen.getByRole('link', { name: 'Review source mapping' })).toHaveAttribute('href', '/systems/system-a/profile/SystemDesign');
  });
  it('renders self-contained diagram artifacts from actual generated back matter without interpreting SVG as page markup', async () => {
    // Arrange
    const image = btoa('<svg xmlns="http://www.w3.org/2000/svg"><text>Approved fixture revision 2</text></svg>');
    vi.mocked(api.getSspPreview).mockResolvedValue({ ...preview, sourceGaps: [], content: JSON.stringify({
      'system-security-plan': {
        metadata: { title: 'SSP with design artifacts' },
        'back-matter': { resources: ['Context', 'Boundary', 'Network', 'Data flows'].map((view, index) => ({
          uuid: `diagram-${index}`, title: `${view} diagram — revision 2`, description: 'Retained approved design fixture.',
          base64: { filename: `${index}.svg`, 'media-type': 'image/svg+xml', value: image },
        })) },
      },
    }) });
    // Act
    mount();
    // Assert
    for (const view of ['Context', 'Boundary', 'Network', 'Data flows']) {
      expect(await screen.findByRole('img', { name: `${view} diagram — revision 2` }))
        .toHaveAttribute('src', `data:image/svg+xml;base64,${image}`);
    }
    expect(screen.getByRole('region', { name: 'Generated diagram artifacts' })).toBeVisible();
    expect(document.querySelectorAll('svg text')).toHaveLength(0);
  });
  it('presents a formal working document without inventing version history or official approval', async () => {
    // Arrange / Act
    mount();
    await screen.findByRole('heading', { name: 'Generated DEMO SSP' });
    // Assert
    expect(screen.getByRole('region', { name: 'SSP cover page' })).toHaveTextContent('WORKING DRAFT');
    expect(screen.getByRole('region', { name: 'SSP cover page' })).toHaveTextContent('Not an official FedRAMP template or approval.');
    expect(screen.getByRole('table', { name: 'Document control' })).toHaveTextContent('Document versionNot recorded');
    expect(screen.getByRole('heading', { name: 'Table of contents' })).toBeVisible();
    expect(screen.getByText(/No revision history was supplied/)).toBeVisible();
  });

  it('shows every generated section and value even when opened from the Users contribution', async () => {
    // Arrange
    const profile = (sectionType: string, content: object) => ({ name: 'working-profile', value: JSON.stringify({
      sectionType, content: { scalarContent: JSON.stringify(content) },
    }) });
    const ssp = {
      uuid: 'document-identity',
      metadata: { title: 'Complete generated SSP', parties: [{ name: 'Responsible organization', uuid: 'party-identity' }] },
      'import-profile': { href: 'baseline-profile-reference' },
      'system-characteristics': { 'system-name': 'Full mission system', description: 'Full system description',
        'authorization-boundary': { description: 'Recorded production boundary' },
        props: [profile('MissionAndPurpose', { missionStatement: 'Mission contribution' }),
          profile('UsersAndAccess', { accessOverview: 'Users contribution' }),
          profile('EnvironmentAndDeployment', { hostingModel: 'Hosting contribution' })] },
      'system-implementation': { components: [{ uuid: 'component-identity', title: 'Provider identity service' }],
        'inventory-items': [{ description: 'Included application resource' }], users: [{ title: 'Assigned system owner' }] },
      'control-implementation': { 'implemented-requirements': [{ 'control-id': 'ac-2', statements: [{ description: 'Actual control narrative' }] }] },
      'back-matter': { resources: [{ title: 'Evidence reference', rlinks: [{ href: 'evidence-reference-uri' }] }] },
      'future-extension': { newValue: 'Previously unknown contributing field', empty: '', count: 0, enabled: false, missing: null },
    };
    const documentData = { $schema: 'generated-schema-reference', 'system-security-plan': ssp };
    vi.mocked(api.getSspPreview).mockResolvedValue({ ...preview, content: JSON.stringify(documentData) });
    // Act
    mount('?contribution=UsersAndAccess');
    await screen.findByRole('heading', { name: 'Complete generated SSP', level: 2 });
    // Assert
    for (const name of ['Mission & purpose', 'Users & access', 'Environment & hosting']) {
      expect(screen.getByRole('heading', { name })).toBeVisible();
    }
    const checkValues = (value: unknown, path: string) => {
      if (Array.isArray(value)) return value.forEach((item, index) => checkValues(item, `${path}/${index}`));
      if (value && typeof value === 'object') return Object.entries(value).forEach(([key, item]) =>
        checkValues(item, `${path}/${key.replace(/~/g, '~0').replace(/\//g, '~1')}`));
      const elements = [...document.querySelectorAll<HTMLElement>('[data-ssp-value-path]')].filter(element => element.dataset.sspValuePath === path);
      expect(elements, path).toHaveLength(1);
      expect(elements[0]!.textContent, path).toBe(value === null || value === '' ? 'Not recorded' : String(value));
    };
    checkValues(documentData, '');
    expect(screen.getByRole('navigation', { name: 'SSP document sections' })).toBeVisible();
  });

  it('preserves explicitly supported legacy plain-text scalar contributions', async () => {
    // Arrange
    vi.mocked(api.getSspPreview).mockResolvedValue({ ...preview, content: JSON.stringify({
      'system-security-plan': { 'system-characteristics': { props: [{
        name: 'working-profile', value: JSON.stringify({ sectionType: 'UsersAndAccess',
          content: { scalarContent: 'Legacy access model retained verbatim.', userCategories: [] } }),
      }] } },
    }) });
    // Act
    mount('?contribution=UsersAndAccess');
    // Assert
    expect(await screen.findByText('Legacy access model retained verbatim.')).toBeVisible();
    expect(screen.getByText('Unstructured saved text')).toBeVisible();
  });

  it('renders retained approved contribution fields and their source references without relabeling them as drafts', async () => {
    // Arrange
    vi.mocked(api.getSspPreview).mockResolvedValue({ ...preview, sourceGaps: [], sourceManifest: {
      scope: 'ProfileAndProvider', profiles: [{ kind: 'ApprovedProfile', recordId: 'profile-a', versionId: 'approved-a', contentHash: 'source-hash' }],
      providerSources: [], narratives: [], otherSources: 'Recorded sources',
    }, content: JSON.stringify({ 'system-security-plan': { 'system-characteristics': {
      props: [{ name: 'approved-profile', value: JSON.stringify({ sectionType: 'MissionAndPurpose',
        Content: { missionStatement: 'Approved mission content', enabled: false, reviewed: true, note: null, references: [] } }) }],
    } } }) });
    // Act
    mount('?contribution=MissionAndPurpose');
    // Assert
    expect(await screen.findAllByText('Approved mission content')).toHaveLength(2);
    expect(screen.getByText('Approved source')).toBeVisible();
    expect(screen.getAllByText('Yes')).toHaveLength(2);
    expect(screen.getAllByText('No')).toHaveLength(2);
    expect(screen.getByText('Profile sources (1)')).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review source mapping' })).toHaveAttribute('href', '/systems/system-a/profile/MissionAndPurpose');
  });

  it('clears stale generated content when refresh fails and retries without a success-shaped fallback', async () => {
    // Arrange
    vi.mocked(api.getSspPreview).mockResolvedValueOnce(preview).mockRejectedValueOnce(new Error('Fresh source lookup failed')).mockResolvedValueOnce(preview);
    mount();
    await screen.findAllByText('Exact generated mission description');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh preview' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Fresh source lookup failed');
    expect(screen.queryByText('Exact generated mission description')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findAllByText('Exact generated mission description')).toHaveLength(2);
  });

  it('reports a requested contribution missing from the generated document instead of substituting registration text', async () => {
    // Arrange / Act
    mount('?contribution=UsersAndAccess');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('No Users & access contribution was returned');
  });

  it('surfaces malformed generated profile extensions', async () => {
    // Arrange
    vi.mocked(api.getSspPreview).mockResolvedValue({ ...preview, content: JSON.stringify({
      'system-security-plan': { 'system-characteristics': { props: [{ name: 'working-profile', value: null }] } },
    }) });
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('no readable source payload');
  });

  it('shows the selected generated working profile and refreshes changed saved contribution content', async () => {
    // Arrange
    const makePreview = (name: string) => ({ ...preview, content: JSON.stringify({ 'system-security-plan': {
      metadata: { title: 'Generated DEMO SSP' }, 'system-characteristics': {
        'system-name': 'DEMO Mission', description: 'Registration description',
        props: [{ name: 'working-profile', value: JSON.stringify({ sectionType: 'UsersAndAccess', sourceState: 'CurrentWorkingData',
          governanceStatus: 'Draft', reviewScope: 'AccessContext', content: { scalarContent: '{"accessOverview":"Saved access model"}',
            userCategories: [{ categoryName: name, approximateCount: 12, accessMethod: 'CAC/PIV', governanceStatus: 'Draft' }] } }) }],
      },
    } }) });
    vi.mocked(api.getSspPreview).mockResolvedValueOnce(makePreview('Original population')).mockResolvedValueOnce(makePreview('Updated population'));
    // Act
    mount('?contribution=UsersAndAccess');
    // Assert
    expect(await screen.findByRole('heading', { name: 'Users & access' })).toBeVisible();
    expect(screen.getByText('Original population')).toBeVisible();
    expect(screen.getByText('Saved access model')).toBeVisible();
    expect(screen.getByText(/Access context: Draft/)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review source mapping' })).toHaveAttribute('href', '/systems/system-a/profile/UsersAndAccess');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh preview' }));
    // Assert
    expect(await screen.findByText('Updated population')).toBeVisible();
    expect(screen.queryByText('Original population')).not.toBeInTheDocument();
  });

  it('does not allow a review-only retained working preview to become a final export', async () => {
    // Arrange
    vi.mocked(api.retainSspPreview).mockResolvedValue({ ...preview, previewId: 'working-a', sourceGaps: [], canGenerate: false });
    mount();
    await screen.findByRole('heading', { name: 'Generated DEMO SSP' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retain generated preview' }));
    const exportButton = await screen.findByRole('button', { name: 'Export retained OSCAL' });
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed this exact retained preview and its source diagnostics.' }));
    // Assert
    expect(exportButton).toBeDisabled();
    expect(screen.getByText(/Working previews are review-only/)).toBeVisible();
  });

  it('renders actual generated fields and source gaps without claiming approved readiness', async () => {
    // Arrange / Act
    mount();
    // Assert
    expect(await screen.findByRole('heading', { name: 'Generated DEMO SSP' })).toBeVisible();
    expect(screen.getAllByText('Exact generated mission description')).toHaveLength(2);
    expect(screen.getByText('Reviewed issuer metadata is missing.')).toBeVisible();
    expect(screen.getByText(/Current working data preview/)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review source mapping' })).toHaveAttribute('href', '/systems/system-a/profile/EnvironmentAndDeployment');
    expect(screen.getByText('retained-content-hash')).toBeVisible();
  });

  it('exposes the original generated OSCAL rather than assembling a browser export', async () => {
    // Arrange
    mount();
    await screen.findByRole('heading', { name: 'Generated DEMO SSP' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'OSCAL source' }));
    // Assert
    expect(screen.getByLabelText('Generated OSCAL JSON').textContent).toBe(preview.content);
  });

  it('shows unavailable with retry and never invents document content', async () => {
    // Arrange
    vi.mocked(api.getSspPreview).mockRejectedValueOnce(new Error('Preview access denied'));
    mount();
    expect(await screen.findByRole('alert')).toHaveTextContent('Preview access denied');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Generated DEMO SSP' })).toBeVisible();
  });

  it('rejects content that does not contain a generated SSP', async () => {
    // Arrange
    vi.mocked(api.getSspPreview).mockResolvedValue({ ...preview, content: '{}' });
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('does not contain an OSCAL system security plan');
  });

  it('requires inspection and confirmation of the returned retained snapshot before export', async () => {
    // Arrange
    vi.mocked(api.retainSspPreview).mockResolvedValue({ ...preview, previewId: 'preview-a', sourceGaps: [] });
    mount();
    await screen.findByRole('heading', { name: 'Generated DEMO SSP' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retain generated preview' }));
    // Assert
    const exportButton = await screen.findByRole('button', { name: 'Export retained OSCAL' });
    expect(exportButton).toBeDisabled();
    expect(api.retainSspPreview).toHaveBeenCalledWith('system-a', expect.any(String), expect.any(AbortSignal));
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed this exact retained preview and its source diagnostics.' }));
    fireEvent.click(exportButton);
    // Assert
    expect(screen.getByRole('dialog')).toHaveTextContent('Export retained preview-a');
  });
});
