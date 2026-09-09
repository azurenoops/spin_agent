#!/usr/bin/env bash
# Sourced by .github/workflows/deploy-containerapp-stage.yml.
# Wait until a Container App is idle, then retry only known-transient Azure
# errors (ContainerAppOperationInProgress, HTTP 429 / Too Many Requests).
# Unexpected errors are returned to the caller — never swallowed.
#
# Refs: #872 (run 33883971872), throttle run 33771928221.

wait_containerapp_idle() {
  local rg="${1:?resource group required}"
  local app="${2:?container app name required}"
  local max_attempts="${3:-36}"
  local sleep_s="${4:-10}"
  local attempt=1
  local state

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

  echo "::error::Timed out waiting for Container App '${app}' to leave provisioning/in-progress state."
  return 1
}

az_containerapp_retry() {
  local max_attempts=8
  local backoff=10
  local attempt=1
  local tmp
  tmp="$(mktemp)"

  while [ "${attempt}" -le "${max_attempts}" ]; do
    if "$@" >"${tmp}" 2>&1; then
      cat "${tmp}"
      rm -f "${tmp}"
      return 0
    fi
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
