#!/usr/bin/env bash
# Sourced by .github/workflows/deploy-containerapp-stage.yml.
# Wait until a Container App is idle, then retry only known-transient Azure
# errors (ContainerAppOperationInProgress, HTTP 429 / Too Many Requests).
# Unexpected errors are returned to the caller — never swallowed.
#
# Refs: #872 (run 33883971872), throttle run 33771928221,
# revision LRO expire runs 34387809098 / 34870272068.

wait_containerapp_idle() {
  local rg="${1:?resource group required}"
  local app="${2:?container app name required}"
  local max_attempts="${3:-36}"
  local sleep_s="${4:-10}"
  local allow_inprogress=0
  local attempt=1
  local state=""

  case "${5:-}" in
    allow-inprogress|1)
      allow_inprogress=1
      ;;
  esac

  while [ "${attempt}" -le "${max_attempts}" ]; do
    state="$(az containerapp show -g "${rg}" -n "${app}" --query properties.provisioningState -o tsv 2>/dev/null || echo "Unknown")"
    case "${state}" in
      Succeeded)
        echo "Container App '${app}' is idle (provisioningState=${state})."
        return 0
        ;;
      Failed|Canceled)
        # Run 34366187254: treating Failed as fatal blocked every later
        # mutation. Failed/Canceled mean the previous ARM op is finished —
        # no OperationInProgress. Waiting cannot reach Succeeded; update
        # is the recovery path (run 34300443590 left MCP Failed).
        echo "::warning::Container App '${app}' is idle in terminal state '${state}'. Proceeding so CD can recover with an update."
        return 0
        ;;
      *)
        echo "Container App '${app}' provisioningState=${state}; waiting ${sleep_s}s (attempt ${attempt}/${max_attempts})."
        sleep "${sleep_s}"
        attempt=$((attempt + 1))
        ;;
    esac
  done

  # Run 34635028916: --no-wait left InProgress; the next wait killed the
  # job before identity/registry/secrets. Config mutations pass
  # allow-inprogress and continue. Unknown / missing app / auth still fail.
  if [ "${allow_inprogress}" -eq 1 ]; then
    case "${state}" in
      InProgress|Updating)
        dump_containerapp_diagnostics "${rg}" "${app}"
        echo "::warning::Timed out with provisioningState=${state} on '${app}'. Proceeding with config mutations so identity/registry/secrets can run (run 34635028916)."
        return 0
        ;;
    esac
  fi

  echo "::error::Timed out waiting for Container App '${app}' to leave provisioning/in-progress state."
  return 1
}

# Run 34635028916: a Failed --no-wait image update starts the revision LRO
# and the next step waits until timeout. Skip that first image patch so
# recovery mutations stay possible. Image is applied after registry/secrets.
containerapp_skip_first_image_update() {
  case "${1:-}" in
    Failed|Canceled|InProgress|Updating)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

# Run 34387809098: az containerapp update on a Failed app blocked ~20m on the
# revision LRO and died with "Failed to provision revision / Operation expired".
# --no-wait accepts the ARM patch without waiting for a healthy revision
# (identity/registry are applied later; smoke check is the health gate).
# Do not delete+recreate: that rotates the system identity and re-hits #873.
#
# Run 35108043923: only create/update accept --no-wait. Passing the flag to
# ingress, identity, registry, or secret set prints
# "ERROR: unrecognized arguments: --no-wait" and kills set -e.
containerapp_command_supports_no_wait() {
  case "${1:-}" in
    create|update) return 0 ;;
    *) return 1 ;;
  esac
}

containerapp_no_wait_if_terminal() {
  local state="${1:-}"
  local command="${2:-}"
  case "${state}" in
    Failed|Canceled)
      if containerapp_command_supports_no_wait "${command}"; then
        printf '%s\n' --no-wait
      fi
      ;;
  esac
}

is_revision_lro_failure() {
  # Run 34870272068: match Operation expired / Failed to provision revision
  # case-insensitively so ingress continue-on-expire actually fires.
  printf '%s' "${1:-}" | grep -qiE 'Failed to provision revision|Operation expired'
}

# Run 34870272068: latestRevision 0000113 was Active + Unhealthy /
# ActivationFailed while latestReady 0000087 stayed Healthy. Ingress
# mutations then waited ~20m for a revision LRO Azure cannot finish.
# Never deactivate the latest ready revision. Never delete the app
# (that rotates the system MI and re-hits #873).
containerapp_should_deactivate_revision() {
  local name="${1:-}"
  local active="${2:-}"
  local health="${3:-}"
  local running="${4:-}"
  local latest_ready="${5:-}"

  if [ -z "${name}" ]; then
    return 1
  fi
  if [ -n "${latest_ready}" ] && [ "${name}" = "${latest_ready}" ]; then
    return 1
  fi
  case "${active}" in
    true|True|TRUE) ;;
    *) return 1 ;;
  esac
  case "${health}" in
    Unhealthy|unhealthy) return 0 ;;
  esac
  case "${running}" in
    ActivationFailed|activationfailed) return 0 ;;
  esac
  return 1
}

containerapp_should_skip_ingress_port() {
  local current="${1:-}"
  local desired="${2:-}"
  [ -n "${current}" ] && [ -n "${desired}" ] && [ "${current}" = "${desired}" ]
}

containerapp_should_skip_ingress_affinity() {
  local current="${1:-}"
  local desired="${2:-sticky}"
  [ -n "${current}" ] && [ "${current}" = "${desired}" ]
}

containerapp_has_system_identity() {
  case "${1:-}" in
    *SystemAssigned*) return 0 ;;
    *) return 1 ;;
  esac
}

containerapp_registry_already_bound() {
  local identity="${1:-}"
  local expected="${2:-system}"
  [ -n "${identity}" ] && [ "${identity}" = "${expected}" ]
}

# Well-known AcrPull built-in role definition GUID.
# https://learn.microsoft.com/azure/role-based-access-control/built-in-roles
ACRPULL_ROLE_DEFINITION_ID="7f951dda-4ed3-4680-a7ca-43fe172d538d"

# Match AcrPull by display name or role definition GUID (full ARM id or bare GUID).
# Run 34989521743: roleDefinitionName=='AcrPull' alone is a false negative when
# Graph does not populate the name (issue #873) but the GUID is still present.
containerapp_is_acrpull_role() {
  local name="${1:-}"
  local role_id="${2:-}"
  role_id="${role_id##*/}"
  case "${name}" in
    AcrPull|acrpull|ACRPULL) return 0 ;;
  esac
  [ -n "${role_id}" ] && [ "${role_id}" = "${ACRPULL_ROLE_DEFINITION_ID}" ]
}

# JMESPath for `az role assignment list` after `--assignee-object-id` (ARM, not Graph --assignee).
containerapp_acrpull_jmespath() {
  printf '%s' "[?(roleDefinitionName=='AcrPull' || contains(to_string(roleDefinitionId), '${ACRPULL_ROLE_DEFINITION_ID}'))].id"
}

# Pull already works if the app is bound to this ACR with the pull identity,
# or a latestReady revision is Healthy (it pulled an image successfully).
containerapp_acrpull_pull_already_works() {
  local bound_identity="${1:-}"
  local expected_identity="${2:-system}"
  local latest_ready="${3:-}"
  local latest_ready_health="${4:-}"

  if containerapp_registry_already_bound "${bound_identity}" "${expected_identity}"; then
    return 0
  fi
  if [ -n "${latest_ready}" ]; then
    case "${latest_ready_health}" in
      Healthy|healthy) return 0 ;;
    esac
  fi
  return 1
}

# Run 34989521743: OIDC cannot write roleAssignments. Do not hard-fail when
# an assignment is present or the app is already pulling from this ACR.
# Fail only when there is no assignment, no registry bind, and no healthy latestReady.
containerapp_acrpull_continue_on_authorization_failed() {
  local has_assignment="${1:-0}"
  local bound_identity="${2:-}"
  local expected_identity="${3:-system}"
  local latest_ready="${4:-}"
  local latest_ready_health="${5:-}"

  case "${has_assignment}" in
    1|true|yes) return 0 ;;
  esac
  containerapp_acrpull_pull_already_works \
    "${bound_identity}" "${expected_identity}" \
    "${latest_ready}" "${latest_ready_health}"
}

deactivate_stuck_containerapp_revisions() {
  local rg="${1:?resource group required}"
  local app="${2:?container app name required}"
  local latest_ready=""
  local name active health running

  latest_ready="$(az containerapp show -g "${rg}" -n "${app}" --query properties.latestReadyRevisionName -o tsv 2>/dev/null || true)"
  if [ -z "${latest_ready}" ]; then
    echo "::warning::No latestReadyRevision on '${app}'; refusing to deactivate revisions (run 34870272068)."
    return 0
  fi

  while IFS=$'\t' read -r name active health running; do
    [ -z "${name}" ] && continue
    if containerapp_should_deactivate_revision "${name}" "${active}" "${health}" "${running}" "${latest_ready}"; then
      echo "::warning::Deactivating stuck revision '${name}' (health=${health}, running=${running}) so Azure can provision a new revision. Keeping latestReady='${latest_ready}' (run 34870272068)."
      if az containerapp revision deactivate -g "${rg}" -n "${app}" --revision "${name}"; then
        echo "Deactivated stuck revision '${name}'."
      else
        echo "::warning::Could not deactivate revision '${name}'. Continuing so later steps can skip no-op mutations and apply the image without waiting on the LRO."
      fi
    fi
  done < <(az containerapp revision list -g "${rg}" -n "${app}" \
    --query "[].{name:name,active:properties.active,health:properties.healthState,running:properties.runningState}" \
    -o tsv)
}

dump_containerapp_diagnostics() {
  local rg="${1:?resource group required}"
  local app="${2:?container app name required}"

  echo "=== Container App diagnostics: ${app} ==="
  if ! az containerapp show -g "${rg}" -n "${app}" \
    --query "{provisioningState:properties.provisioningState,runningStatus:properties.runningStatus,latestRevision:properties.latestRevisionName,latestReadyRevision:properties.latestReadyRevisionName,identityType:identity.type,registries:properties.configuration.registries}" \
    -o json; then
    echo "::warning::Could not show Container App '${app}' for diagnostics."
  fi
  echo "=== Revisions ==="
  if ! az containerapp revision list -g "${rg}" -n "${app}" \
    --query "[].{name:name,active:properties.active,health:properties.healthState,running:properties.runningState,replicas:properties.replicas,provisioning:properties.provisioningState,created:properties.createdTime}" \
    -o table; then
    echo "::warning::Could not list revisions for '${app}'."
  fi
}

az_containerapp_retry() {
  local max_attempts=8
  local backoff=10
  local attempt=1
  local tmp
  tmp="$(mktemp)"
  AZ_CONTAINERAPP_LAST_OUTPUT=""

  while [ "${attempt}" -le "${max_attempts}" ]; do
    if "$@" >"${tmp}" 2>&1; then
      AZ_CONTAINERAPP_LAST_OUTPUT="$(cat "${tmp}")"
      cat "${tmp}"
      rm -f "${tmp}"
      return 0
    fi
    AZ_CONTAINERAPP_LAST_OUTPUT="$(cat "${tmp}")"
    cat "${tmp}"
    if grep -qE 'ContainerAppOperationInProgress|Too Many Requests|ErrorCode: 429' "${tmp}"; then
      echo "::warning::Transient Azure error (OperationInProgress or throttle) on attempt ${attempt}/${max_attempts}; backing off ${backoff}s."
      sleep "${backoff}"
      backoff=$((backoff * 2))
      if [ "${backoff}" -gt 80 ]; then
        backoff=80
      fi
      attempt=$((attempt + 1))
      continue
    fi
    rm -f "${tmp}"
    return 1
  done

  rm -f "${tmp}"
  echo "::error::Exhausted ${max_attempts} retries for: $*"
  return 1
}

# Run 34387809098: a revision LRO timeout is not a missing app. Identity,
# AcrPull, and registry bind happen after this step; failing here left MCP
# Failed and skipped every later environment. Continue only for that LRO
# class, and only if the app resource still exists.
az_containerapp_retry_continue_revision_lro() {
  local rg="${1:?resource group required}"
  local app="${2:?container app name required}"
  shift 2

  if az_containerapp_retry "$@"; then
    return 0
  fi

  dump_containerapp_diagnostics "${rg}" "${app}"
  if is_revision_lro_failure "${AZ_CONTAINERAPP_LAST_OUTPUT:-}"; then
    if az containerapp show -g "${rg}" -n "${app}" >/dev/null 2>&1; then
      echo "::warning::Revision provision did not finish (Operation expired / Failed to provision revision). App '${app}' still exists. Continuing so later steps can bind registry/identity/secrets and reapply the image."
      return 0
    fi
    echo "::error::Revision provision expired and Container App '${app}' is gone."
    return 1
  fi
  return 1
}
