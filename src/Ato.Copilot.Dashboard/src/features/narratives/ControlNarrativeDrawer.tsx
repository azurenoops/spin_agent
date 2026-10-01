import { useCallback, useEffect, useRef, useState } from 'react';
import { ChevronDown, ExternalLink, FileText, Info, UserRound, UsersRound, X } from 'lucide-react';
import type {
  ControlNarrativeDetailResponse,
  NarrativeStatementKind,
  NarrativeStatementDetail,
} from '../../api/controlNarrativeWorkspace';
import ValidationEvidencePanel from '../compliance/components/ValidationEvidencePanel';
import RequirementCoveragePanel from './RequirementCoveragePanel';

interface Props {
  detail: ControlNarrativeDetailResponse;
  initialStatement: NarrativeStatementKind;
  onStatementChange: (kind: NarrativeStatementKind) => void;
  onClose: () => void;
  onEdit: () => void;
  onViewSource: () => void;
  onReviewProposal: (proposalId: string) => void;
  onNavigate: (controlId: string) => void;
  onChanged: () => void;
}

type DrawerTab = 'statements' | 'evidence' | 'history';

function readableLabel(value: string): string {
  return value.replace(/[-_]/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/^./, first => first.toUpperCase());
}

function StatementContent({ statement, proposal, kind, canAuthor, canReview, onEdit, onReviewProposal }: {
  statement: NarrativeStatementDetail;
  proposal: ControlNarrativeDetailResponse['proposals'][number] | null;
  kind: NarrativeStatementKind;
  canAuthor: boolean;
  canReview: boolean;
  onEdit: () => void;
  onReviewProposal: (proposalId: string) => void;
}) {
  return <div className="cnw-statement-content">
    {proposal && <div className="cnw-proposal-status">
      <span>▲</span> Proposed update · {proposal.isStale ? 'Source changed' : 'Not approved'}
    </div>}
    <section className="cnw-statement-card" aria-labelledby={`${kind}-current-heading`}>
      <h3 id={`${kind}-current-heading`}>Current saved {statement.state === 'Approved' ? 'statement' : 'draft'}</h3>
      {statement.currentContent?.trim()
        ? <p className="cnw-narrative-text">{statement.currentContent}</p>
        : <div className="cnw-empty-state"><strong>No {kind} statement has been saved.</strong>
          <p>Add a statement only when you have source-supported implementation details.</p></div>}
      {statement.approvedContent?.trim() && statement.approvedContent !== statement.currentContent &&
        <details><summary>View retained approved statement</summary>
          <p className="cnw-narrative-text">{statement.approvedContent}</p></details>}
    </section>

    {proposal && <section className="cnw-statement-card cnw-proposed-card" aria-labelledby={`${kind}-proposal-heading`}>
      <h3 id={`${kind}-proposal-heading`}>Proposed statement</h3>
      <p className="cnw-narrative-text">{proposal.proposedContent || 'No proposed content.'}</p>
      <small>Generated draft — verify against evidence.</small>
    </section>}
    {proposal && <section className="cnw-change-reason">
      <h3>Why this changed</h3>
      <p>{proposal.cause
        ? `A ${readableLabel(proposal.cause).toLowerCase()} source record changed. Confirm the affected services and implementation details.`
        : 'A source record changed. Confirm the affected services and implementation details.'}</p>
      {proposal.conflicts.length > 0 && <div className="cnw-blocked"><strong>Conflicts to resolve</strong>
        <ul>{proposal.conflicts.map(conflict => <li key={conflict}>{conflict}</li>)}</ul></div>}
      {proposal.missingEvidence.length > 0 && <div className="cnw-blocked"><strong>Supporting evidence needed</strong>
        <ul>{proposal.missingEvidence.map(gap => <li key={gap}>{gap}</li>)}</ul></div>}
      <details className="cnw-drawer-accordion"><summary><FileText size={17} />Sources &amp; supporting evidence<ChevronDown size={16} /></summary>
        {Object.keys(proposal.provenance).length === 0
          ? <p className="cnw-muted">No source provenance was retained for this proposal.</p>
          : <dl className="cnw-provenance">{Object.entries(proposal.provenance).map(([key, value]) =>
            <div key={key}><dt>{key.replace(/([A-Z])/g, ' $1')}</dt><dd>{typeof value === 'string' ? value : JSON.stringify(value)}</dd></div>)}</dl>}
      </details>
      {canReview && proposal.canReview && !proposal.isStale
        ? <button type="button" className="cnw-primary" onClick={() => onReviewProposal(proposal.id)}>Review proposed change</button>
        : <p className="cnw-action-note">{proposal.isStale
          ? 'This proposal cannot be approved because its source or narrative version changed. Generate a current proposal first.'
          : proposal.reviewReason || 'Review is not available for your current system assignment.'}</p>}
    </section>}

    {!proposal && canAuthor && <button type="button" className="cnw-primary" onClick={onEdit}>
      {statement.currentContent?.trim() ? `Edit ${kind} statement` : `Add ${kind} statement`}
    </button>}
    {!proposal && !canAuthor && <p className="cnw-action-note">You can inspect this statement, but authoring is not available for your current system assignment.</p>}
  </div>;
}

export default function ControlNarrativeDrawer({
  detail, initialStatement, onStatementChange, onClose, onEdit, onViewSource, onReviewProposal, onNavigate, onChanged,
}: Props) {
  const [tab, setTab] = useState<DrawerTab>('statements');
  const [statement, setStatement] = useState<NarrativeStatementKind>(initialStatement);
  const closeRef = useRef<HTMLButtonElement>(null);
  const drawerRef = useRef<HTMLElement>(null);
  const unsaved = useRef(false);
  const onDirtyChange = useCallback((dirty: boolean) => { unsaved.current = dirty; }, []);
  const requestClose = useCallback(() => {
    if (!unsaved.current || window.confirm('Discard unsaved requirement responses?')) onClose();
  }, [onClose]);

  useEffect(() => {
    closeRef.current?.focus();
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') requestClose();
      if (event.key !== 'Tab' || !drawerRef.current) return;
      const focusable = Array.from(drawerRef.current.querySelectorAll<HTMLElement>(
        'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), summary',
      ));
      if (focusable.length === 0) return;
      const first = focusable[0]!;
      const last = focusable[focusable.length - 1]!;
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [requestClose]);

  const selectStatement = (next: NarrativeStatementKind) => {
    setStatement(next);
    onStatementChange(next);
  };

  return <div className="cnw-drawer-layer">
    <button type="button" className="cnw-scrim" aria-label="Close control details" onClick={requestClose} />
    <aside ref={drawerRef} className="cnw-drawer" role="dialog" aria-modal="true"
      aria-label={`${detail.controlId} ${detail.controlTitle}`}>
      <header className="cnw-drawer-header">
        <div><span className="cnw-drawer-kicker">Control detail · opened on selection</span>
          <h2>{detail.controlId} · {detail.controlTitle}</h2></div>
        <button ref={closeRef} type="button" className="cnw-icon-button" aria-label="Close control details" onClick={requestClose}><X size={20} /></button>
      </header>
      <div className="cnw-tabs" role="tablist" aria-label="Control detail sections">
        {(['statements', 'evidence', 'history'] as const).map(value =>
          <button key={value} type="button" role="tab" aria-selected={tab === value}
            onClick={() => setTab(value)}>{value.charAt(0).toUpperCase() + value.slice(1)}</button>)}
      </div>
      <div className="cnw-drawer-body">
        <div hidden={tab !== 'statements'}>
          <RequirementCoveragePanel key={`${detail.systemId}:${detail.controlId}`} systemId={detail.systemId}
            controlId={detail.controlId} kind={statement} onNavigate={onNavigate} onChanged={onChanged} onDirtyChange={onDirtyChange} />
          <div className="cnw-statement-tabs" role="tablist" aria-label="Narrative statement type">
            <button type="button" role="tab" aria-label="Policy statement" aria-selected={statement === 'policy'} onClick={() => selectStatement('policy')}>Policy</button>
            <button type="button" role="tab" aria-label="Technical statement" aria-selected={statement === 'technical'} onClick={() => selectStatement('technical')}>Technical</button>
          </div>
          <StatementContent statement={detail.statements[statement]}
            proposal={detail.proposals.find(item => item.narrativeType.toLowerCase() === statement) ?? null}
            kind={statement} canAuthor={detail.permissions.canAuthor} canReview={detail.permissions.canReview}
            onEdit={onEdit} onReviewProposal={onReviewProposal} />
          <details className="cnw-drawer-accordion cnw-dependencies"><summary><UsersRound size={17} />Responsibility dependencies<ChevronDown size={16} /></summary>
            {detail.responsibilities.length === 0
              ? <p className="cnw-muted">No responsibility dependencies are recorded for this control.</p>
              : <ul>{detail.responsibilities.map(dependency =>
                <li key={dependency.id}><strong>{dependency.provider || dependency.source || 'System responsibility'}</strong>
                  <span>{dependency.customerResponsibility || 'No customer responsibility text recorded.'}</span>
                  <small>{dependency.inheritanceType}</small></li>)}</ul>}
          </details>
          {detail.proposals.some(item => item.narrativeType.toLowerCase() === statement) &&
            <div className="cnw-retention-note"><Info size={17} />Current content stays unchanged until an authorized reviewer accepts the proposal.</div>}
        </div>
        {tab === 'evidence' && <ValidationEvidencePanel systemId={detail.systemId}
          controlId={detail.controlId} canManage={detail.permissions.canManageEvidence} />}
        {tab === 'history' && <section className="cnw-history" aria-label="Narrative history">
          {detail.history.length === 0
            ? <div className="cnw-empty-state"><strong>No narrative history is available.</strong></div>
            : detail.history.map(version => <article key={version.versionNumber}>
              <div><strong>Version {version.versionNumber}</strong><span className="cnw-state">{version.status}</span></div>
              <p>{version.changeReason || 'No change reason recorded.'}</p>
              <small>{version.authoredBy || 'Unknown author'} · {new Date(version.authoredAt).toLocaleString()}</small>
              {version.reviews.map((review, index) => <p key={index} className="cnw-review-record">
                {review.decision} by {review.reviewedBy} · {review.comments || 'No review note'}</p>)}
            </article>)}
        </section>}
      </div>
      <footer className="cnw-drawer-footer">
        <span><UserRound size={17} />{detail.permissions.canReview ? 'Reviewer · Approval available'
          : detail.permissions.canAuthor ? 'Author · Editing available' : 'Current role · View only'}</span>
        <button type="button" onClick={requestClose}>Close</button>
        <button type="button" className="cnw-primary" onClick={onViewSource}>View source <ExternalLink size={14} /></button>
      </footer>
    </aside>
  </div>;
}
