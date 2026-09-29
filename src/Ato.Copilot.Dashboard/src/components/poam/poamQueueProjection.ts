import type { PoamWorkspace } from '../../api/poamWorkspace';
import type { PoamListItem, PoamListQuery, PoamStatus, CatSeverity } from '../../types/poam';

export function projectPoamQueue(workspace: PoamWorkspace, query: PoamListQuery = {}, now = new Date()) {
  const timestamp = now.getTime();
  const tasks = new Map(workspace.tasks.map(task => [task.id, task]));
  const rows: PoamListItem[] = workspace.poams.map(poam => {
    if (typeof poam.scheduledCompletionDate !== 'string' || !Number.isFinite(Date.parse(poam.scheduledCompletionDate))
      || typeof poam.pointOfContact !== 'string' || typeof poam.catSeverity !== 'string' || !Array.isArray(poam.milestones)
      || !poam.milestones.every(milestone => typeof milestone.id === 'string' && typeof milestone.description === 'string'
        && Number.isFinite(Date.parse(milestone.targetDate)) && Number.isFinite(milestone.sequence)
        && (milestone.completedDate === null || Number.isFinite(Date.parse(milestone.completedDate)))))
      throw new Error('Commitment queue data is incomplete. Refresh retained milestones and deadlines before continuing.');
    const active = poam.status === 'Ongoing' || poam.status === 'Delayed';
    const milestones = [...poam.milestones].sort((a, b) => Date.parse(a.targetDate) - Date.parse(b.targetDate) || a.sequence - b.sequence).map(milestone => ({
      ...milestone, isOverdue: active && !milestone.completedDate && Date.parse(milestone.targetDate) < timestamp,
    }));
    const nextMilestone = milestones.find(milestone => !milestone.completedDate) ?? null;
    const readyToVerify = active && milestones.length > 0 && !nextMilestone
      && poam.taskIds.every(id => { const task = tasks.get(id); return task?.status === 'Done' && task.verificationStatus === 'Passed'; });
    return {
      id: poam.id, systemId: workspace.systemId, systemName: '', controlId: poam.securityControlNumber,
      weakness: poam.weakness, catSeverity: poam.catSeverity.replace(/^Cat/, '') as CatSeverity,
      status: poam.status as PoamStatus, components: [], poc: poam.pointOfContact, dueDate: poam.scheduledCompletionDate,
      daysRemaining: Math.ceil((Date.parse(poam.scheduledCompletionDate) - timestamp) / 86400000),
      milestoneProgress: { completed: milestones.filter(milestone => milestone.completedDate).length, total: milestones.length },
      nextMilestone, readyToVerify,
      deviationType: poam.deviationId ? 'linked' : null, externalTicketRef: null,
      remediationTaskId: poam.taskIds[0] ?? null, remediationTaskStatus: tasks.get(poam.taskIds[0] ?? '')?.status ?? null,
      isOverdue: active && (Date.parse(poam.scheduledCompletionDate) < timestamp || milestones.some(milestone => milestone.isOverdue)),
    };
  });
  const closed = (item: PoamListItem) => item.status === 'Completed';
  const counts = { all: rows.length, overdue: rows.filter(item => item.isOverdue).length,
    readyToVerify: rows.filter(item => item.readyToVerify).length, closed: rows.filter(closed).length };
  const search = query.search?.trim().toLowerCase();
  const filtered = rows.filter(item => (!query.status || item.status === query.status)
    && (!query.catSeverity || item.catSeverity === query.catSeverity.replace(/^Cat/, ''))
    && (!query.overdue || item.isOverdue)
    && (!search || `${item.weakness} ${item.controlId} ${item.poc}`.toLowerCase().includes(search))
    && (query.view !== 'overdue' || item.isOverdue)
    && (query.view !== 'ready' || item.readyToVerify)
    && (query.view !== 'closed' || closed(item)));
  filtered.sort((a, b) => {
    const left = query.sortBy === 'weakness' ? a.weakness : query.sortBy === 'poc' ? a.poc : a.dueDate;
    const right = query.sortBy === 'weakness' ? b.weakness : query.sortBy === 'poc' ? b.poc : b.dueDate;
    return left.localeCompare(right) * (query.sortDirection === 'desc' ? -1 : 1);
  });
  const pageSize = Math.max(1, Math.min(100, query.pageSize ?? 25));
  const totalPages = Math.ceil(filtered.length / pageSize);
  const page = Math.max(1, Math.min(Math.max(1, totalPages), query.page ?? 1));
  return { items: filtered.slice((page - 1) * pageSize, page * pageSize), totalCount: filtered.length, page, pageSize, totalPages, counts };
}
