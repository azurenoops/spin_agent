import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import SetupDialog from '../workspace-operations/SetupDialog';
import { WorkspaceChoices } from './WorkspacePicker';
import { buildWorkspaceUrl, type WorkspaceTarget } from './workspaceRoutes';
import { optionTarget } from './WorkspaceEntry';
import type { WorkspaceOption } from './types';

export default function WorkspaceSwitchDialog({ currentWorkspace, currentName, onClose }: {
  currentWorkspace: WorkspaceTarget; currentName: string; onClose: () => void;
}) {
  const navigate = useNavigate();
  const [selected, setSelected] = useState<WorkspaceOption | null>(null);
  const select = (option: WorkspaceOption) => {
    if (buildWorkspaceUrl(optionTarget(option)) === buildWorkspaceUrl(currentWorkspace)) {
      onClose();
      return;
    }
    setSelected(option);
  };
  return <SetupDialog title="Switch workspace" description={`Current workspace: ${currentName}. Cancel to stay here without losing your work.`}
    busy={false} onClose={onClose}>
    {selected ? <div className="space-y-4">
      <h3 className="font-semibold">{selected.displayName}</h3>
      <p className="text-sm">Switching leaves this page and discards unsaved changes in this tab. Cancel to stay and save your work first. Other tabs are unchanged.</p>
      <div className="flex flex-wrap gap-3">
        <button type="button" className="rounded border px-3 py-2 text-sm" onClick={() => setSelected(null)}>Choose another workspace</button>
        <button type="button" className="rounded bg-indigo-700 px-3 py-2 text-sm text-white" onClick={() => {
          onClose();
          navigate(buildWorkspaceUrl(optionTarget(selected)));
        }}>Switch to {selected.displayName}</button>
      </div>
    </div> : <WorkspaceChoices onSelect={select} />}
    <button type="button" className="mt-5 rounded border px-3 py-2 text-sm" onClick={onClose}>Cancel</button>
  </SetupDialog>;
}
