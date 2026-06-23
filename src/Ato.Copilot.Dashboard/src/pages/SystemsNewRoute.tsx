import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';

/**
 * SystemsNewRoute — fix(#522)
 *
 * Handles direct navigation to `/systems/new`. Without this route, React
 * Router would match the `/systems/:id` route with `id = "new"`, causing
 * `getSystemDetail("new")` to fail with "Failed to load system detail".
 *
 * This component immediately redirects to `/systems` (the portfolio page)
 * and passes `state.openWizard = true` so that `PortfolioDashboard` can
 * detect the flag and open the intake wizard automatically.
 *
 * The route must be registered **before** `/systems/:id` in `App.tsx` so
 * that React Router matches this route first.
 */
export default function SystemsNewRoute() {
  const navigate = useNavigate();

  useEffect(() => {
    navigate('/systems', { replace: true, state: { openWizard: true } });
  }, [navigate]);

  return null;
}
