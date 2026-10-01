import type { ReactNode } from 'react';

export const sspTemplateHeadings = [
  'Introduction', 'Purpose', 'System Information', 'System Owner',
  'Assignment of Security Responsibility', 'Leveraged FedRAMP-Authorized Services',
  'External Systems and Services Not Having FedRAMP Authorization',
  'Illustrated Architecture and Narratives', 'Services, Ports, and Protocols',
  'Cryptographic Modules Implemented for Data At Rest (DAR) and Data In Transit (DIT)',
  'Separation of Duties', 'SSP Appendices List',
];

function record(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

function hasContent(value: unknown): boolean {
  if (value === null || value === undefined || value === '') return false;
  if (Array.isArray(value)) return value.some(hasContent);
  if (typeof value === 'object') return Object.values(record(value)).some(hasContent);
  return true;
}

type Profile = { sectionType: string; fields: Record<string, unknown>; working: boolean };
type Props = { body: Record<string, unknown>; profiles: Profile[]; renderFields: (value: unknown) => ReactNode };

/** A reading projection; the complete generated appendix remains the source of truth. */
export default function SspTemplateSections({ body, profiles, renderFields }: Props) {
  const characteristics = record(body['system-characteristics']);
  const implementation = record(body['system-implementation']);
  const metadata = record(body.metadata);
  const profile = (type: string) => profiles.filter(item => item.sectionType === type)
    .map(item => ({ source: item.working ? 'Saved working source contribution' : 'Approved source contribution', ...item.fields }));
  const responsibleParties = (role: string) => {
    const assignments = Array.isArray(metadata['responsible-parties']) ? metadata['responsible-parties'] : [];
    const parties = Array.isArray(metadata.parties) ? metadata.parties : [];
    return assignments.filter(item => record(item)['role-id'] === role).map(item => {
      const ids = record(item)['party-uuids'];
      return { assignment: item, parties: parties.filter(party => Array.isArray(ids) && ids.includes(record(party).uuid)) };
    });
  };
  const sections = [
    { value: characteristics.description, gap: 'No system introduction was supplied in the generated document.' },
    { value: profile('MissionAndPurpose'), gap: 'No mission and purpose contribution was supplied in the generated document.' },
    { value: { 'System name': characteristics['system-name'], 'System identifiers': characteristics['system-ids'],
      'Security sensitivity level': characteristics['security-sensitivity-level'],
      'Information types': characteristics['system-information'], 'Security impact level': characteristics['security-impact-level'] },
      gap: 'No system information was supplied in the generated document.' },
    { value: responsibleParties('system-owner'), gap: 'No system owner assignment was supplied in the generated document.' },
    { value: responsibleParties('information-system-security-officer'), gap: 'No ISSO assignment was supplied in the generated document.' },
    { value: implementation['leveraged-authorizations'], gap: 'No leveraged authorization records were supplied in the generated document.',
      note: 'Recorded authorization references require source review. Inclusion here does not establish FedRAMP authorization or accepted inheritance.' },
    { gap: 'The generated document does not classify external services by FedRAMP authorization status.',
      note: 'Review recorded interconnections and provider references before assigning services to this section.' },
    { value: { 'Authorization boundary': characteristics['authorization-boundary'], 'Network architecture': characteristics['network-architecture'],
      'Data flow': characteristics['data-flow'], 'Environment and deployment': profile('EnvironmentAndDeployment') },
      gap: 'No architecture or environment records were supplied in the generated document.' },
    { value: profile('PortsProtocolsAndServices'), gap: 'No services, ports, or protocols contribution was supplied in the generated document.' },
    { gap: 'No cryptographic module records were supplied in the generated document.',
      note: 'Control narratives and component names alone do not establish validated module records.' },
    { gap: 'No separation of duties matrix was supplied in the generated document.' },
    { value: record(body['back-matter']).resources, gap: 'No appendix resources were supplied in the generated document.',
      note: 'Recorded references follow below. Required appendices and attachment completeness still require review.' },
  ];
  return <section aria-label="SSP template sections" className="ssp-template-sections">
    {sections.map((section, index) => <section key={sspTemplateHeadings[index]} id={`ssp-template-${index + 1}`} className="ssp-generated-section">
      <h3>{index + 1}. {sspTemplateHeadings[index]}</h3>
      {section.note && <p className="ssp-section-note">{section.note}</p>}
      {hasContent(section.value) ? renderFields(section.value) : <p className="ssp-missing-content">{section.gap}</p>}
      {index === 11 && <nav aria-label="Generated SSP appendices" className="ssp-toc">
        {profiles.length > 0 && <a href="#ssp-profile-contributions">Recorded profile contributions</a>}
        <a href="#ssp-complete-generated">Complete generated SSP and control implementation records</a>
      </nav>}
    </section>)}
  </section>;
}

export function SspTemplateFrontMatter() {
  return <>
    <section className="ssp-front-matter" aria-label="Prepared by and prepared for">
      <h3>SYSTEM SECURITY PLAN</h3>
      <h4>Prepared by</h4>
      <table className="ssp-control-table"><caption>Identification of Organization that Prepared this Document</caption>
        <tbody><tr><th scope="row">Organization</th><td>Not identified as the document preparer in the generated source.</td></tr></tbody></table>
      <h4>Prepared for</h4>
      <table className="ssp-control-table"><caption>Identification of Cloud Service Provider</caption>
        <tbody><tr><th scope="row">Organization</th><td>Not identified as the document recipient in the generated source.</td></tr></tbody></table>
    </section>
  </>;
}

export function SspTemplateApprovals() {
  return <section className="ssp-front-matter" aria-label="SSP approvals">
      <h3>SYSTEM SECURITY PLAN APPROVALS</h3>
      <p>No approval signatures were supplied in the generated document.</p>
      <p className="ssp-section-note">Recorded responsible parties and review states do not constitute signatures or an authorization decision.</p>
    </section>;
}
