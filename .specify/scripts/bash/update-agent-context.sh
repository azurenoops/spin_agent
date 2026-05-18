#!/usr/bin/env bash

# Update agent context files with information from plan.md
#
# Usage: ./update-agent-context.sh [agent_type]
# Agent types: claude|gemini|copilot|cursor-agent|qwen|opencode|codex|windsurf|kilocode|auggie|roo|codebuddy|amp|shai|q|agy|bob|qodercli
# Leave empty to update all existing agent files

set -e
set -u
set -o pipefail

SCRIPT_DIR="$(CDPATH="" cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

eval $(get_feature_paths)

NEW_PLAN="$IMPL_PLAN"
AGENT_TYPE="${1:-}"

CLAUDE_FILE="$REPO_ROOT/CLAUDE.md"
GEMINI_FILE="$REPO_ROOT/GEMINI.md"
COPILOT_FILE="$REPO_ROOT/.github/copilot-instructions.md"
CURSOR_FILE="$REPO_ROOT/.cursor/rules/specify-rules.mdc"
QWEN_FILE="$REPO_ROOT/QWEN.md"
AGENTS_FILE="$REPO_ROOT/AGENTS.md"
WINDSURF_FILE="$REPO_ROOT/.windsurf/rules/specify-rules.md"
KILOCODE_FILE="$REPO_ROOT/.kilocode/rules/specify-rules.md"
AUGGIE_FILE="$REPO_ROOT/.augment/rules/specify-rules.md"
ROO_FILE="$REPO_ROOT/.roo/rules/specify-rules.md"
CODEBUDDY_FILE="$REPO_ROOT/CODEBUDDY.md"
QODER_FILE="$REPO_ROOT/QODER.md"
AMP_FILE="$REPO_ROOT/AGENTS.md"
SHAI_FILE="$REPO_ROOT/SHAI.md"
Q_FILE="$REPO_ROOT/AGENTS.md"
AGY_FILE="$REPO_ROOT/.agent/rules/specify-rules.md"
BOB_FILE="$REPO_ROOT/AGENTS.md"

TEMPLATE_FILE="$REPO_ROOT/.specify/templates/agent-file-template.md"

NEW_LANG=""
NEW_FRAMEWORK=""
NEW_DB=""
NEW_PROJECT_TYPE=""

log_info() { echo "INFO: $1"; }
log_success() { echo "✓ $1"; }
log_error() { echo "ERROR: $1" >&2; }
log_warning() { echo "WARNING: $1" >&2; }

cleanup() {
    local exit_code=$?
    rm -f /tmp/agent_update_*_$$
    rm -f /tmp/manual_additions_$$
    exit $exit_code
}
trap cleanup EXIT INT TERM

validate_environment() {
    if [[ -z "$CURRENT_BRANCH" ]]; then
        log_error "Unable to determine current feature"
        exit 1
    fi
    if [[ ! -f "$NEW_PLAN" ]]; then
        log_error "No plan.md found at $NEW_PLAN"
        exit 1
    fi
    if [[ ! -f "$TEMPLATE_FILE" ]]; then
        log_warning "Template file not found at $TEMPLATE_FILE"
    fi
}

extract_plan_field() {
    local field_pattern="$1"
    local plan_file="$2"
    grep "^\*\*${field_pattern}\*\*: " "$plan_file" 2>/dev/null | \
        head -1 | sed "s|^\*\*${field_pattern}\*\*: ||" | \
        sed 's/^[ \t]*//;s/[ \t]*$//' | \
        grep -v "NEEDS CLARIFICATION" | grep -v "^N/A$" || echo ""
}

parse_plan_data() {
    local plan_file="$1"
    if [[ ! -f "$plan_file" ]] || [[ ! -r "$plan_file" ]]; then
        log_error "Plan file not found or not readable: $plan_file"
        return 1
    fi
    log_info "Parsing plan data from $plan_file"
    NEW_LANG=$(extract_plan_field "Language/Version" "$plan_file")
    NEW_FRAMEWORK=$(extract_plan_field "Primary Dependencies" "$plan_file")
    NEW_DB=$(extract_plan_field "Storage" "$plan_file")
    NEW_PROJECT_TYPE=$(extract_plan_field "Project Type" "$plan_file")
    [[ -n "$NEW_LANG" ]] && log_info "Found language: $NEW_LANG"
    [[ -n "$NEW_FRAMEWORK" ]] && log_info "Found framework: $NEW_FRAMEWORK"
}

format_technology_stack() {
    local lang="$1"
    local framework="$2"
    local parts=()
    [[ -n "$lang" && "$lang" != "NEEDS CLARIFICATION" ]] && parts+=("$lang")
    [[ -n "$framework" && "$framework" != "NEEDS CLARIFICATION" && "$framework" != "N/A" ]] && parts+=("$framework")
    if [[ ${#parts[@]} -eq 0 ]]; then echo ""
    elif [[ ${#parts[@]} -eq 1 ]]; then echo "${parts[0]}"
    else
        local result="${parts[0]}"
        for ((i=1; i<${#parts[@]}; i++)); do result="$result + ${parts[i]}"; done
        echo "$result"
    fi
}

get_project_structure() {
    local project_type="$1"
    if [[ "$project_type" == *"web"* ]]; then echo "backend/\\nfrontend/\\ntests/"
    else echo "src/\\ntests/"; fi
}

get_commands_for_language() {
    local lang="$1"
    case "$lang" in
        *"C#"*|*"dotnet"*|*".NET"*) echo "dotnet build Ato.Copilot.sln && dotnet test" ;;
        *"Python"*) echo "cd src && pytest && ruff check ." ;;
        *"TypeScript"*|*"JavaScript"*) echo "npm test && npm run lint" ;;
        *) echo "# Add commands for $lang" ;;
    esac
}

create_new_agent_file() {
    local target_file="$1"
    local temp_file="$2"
    local project_name="$3"
    local current_date="$4"
    if [[ ! -f "$TEMPLATE_FILE" ]]; then log_error "Template not found"; return 1; fi
    cp "$TEMPLATE_FILE" "$temp_file"
    local project_structure=$(get_project_structure "$NEW_PROJECT_TYPE")
    local commands=$(get_commands_for_language "$NEW_LANG")
    local escaped_lang=$(printf '%s\n' "$NEW_LANG" | sed 's/[\[\.*^$()+{}|]/\\&/g')
    local escaped_framework=$(printf '%s\n' "$NEW_FRAMEWORK" | sed 's/[\[\.*^$()+{}|]/\\&/g')
    local escaped_branch=$(printf '%s\n' "$CURRENT_BRANCH" | sed 's/[\[\.*^$()+{}|]/\\&/g')
    local tech_stack="- $escaped_lang + $escaped_framework ($escaped_branch)"
    local recent_change="- $escaped_branch: Added $escaped_lang + $escaped_framework"
    sed -i.bak -e "s|\[PROJECT NAME\]|$project_name|" \
        -e "s|\[DATE\]|$current_date|" \
        -e "s|\[EXTRACTED FROM ALL PLAN.MD FILES\]|$tech_stack|" \
        -e "s|\[ACTUAL STRUCTURE FROM PLANS\]|$project_structure|g" \
        -e "s|\[ONLY COMMANDS FOR ACTIVE TECHNOLOGIES\]|$commands|" \
        -e "s|\[LANGUAGE-SPECIFIC, ONLY FOR LANGUAGES IN USE\]|C# .NET 9: Follow standard conventions|" \
        -e "s|\[LAST 3 FEATURES AND WHAT THEY ADDED\]|$recent_change|" \
        "$temp_file"
    rm -f "$temp_file.bak"
    return 0
}

update_existing_agent_file() {
    local target_file="$1"
    local current_date="$2"
    log_info "Updating existing agent context file..."
    local temp_file=$(mktemp) || { log_error "Failed to create temp file"; return 1; }
    local tech_stack=$(format_technology_stack "$NEW_LANG" "$NEW_FRAMEWORK")

    # Build deduped list of new Active Technologies entries.
    # An entry is only added if not already present (exact line match).
    local new_tech_entries=()
    if [[ -n "$tech_stack" ]]; then
        local tech_entry="- $tech_stack ($CURRENT_BRANCH)"
        if ! grep -Fxq "$tech_entry" "$target_file"; then
            new_tech_entries+=("$tech_entry")
        fi
    fi
    if [[ -n "$NEW_DB" && "$NEW_DB" != "N/A" && "$NEW_DB" != "NEEDS CLARIFICATION" ]]; then
        local db_entry="- $NEW_DB ($CURRENT_BRANCH)"
        if ! grep -Fxq "$db_entry" "$target_file"; then
            new_tech_entries+=("$db_entry")
        fi
    fi

    local new_change_entry=""
    if [[ -n "$tech_stack" ]]; then
        new_change_entry="- $CURRENT_BRANCH: Added $tech_stack"
    elif [[ -n "$NEW_DB" && "$NEW_DB" != "N/A" && "$NEW_DB" != "NEEDS CLARIFICATION" ]]; then
        new_change_entry="- $CURRENT_BRANCH: Added $NEW_DB"
    fi

    local in_tech_section=false
    local tech_added=false
    local in_changes_section=false
    local existing_changes_count=0

    while IFS= read -r line || [[ -n "$line" ]]; do
        # --- Active Technologies handling -----------------------------------
        if [[ "$line" == "## Active Technologies" ]]; then
            echo "$line" >> "$temp_file"
            in_tech_section=true
            continue
        fi
        if [[ $in_tech_section == true ]] && [[ "$line" =~ ^##[[:space:]] ]]; then
            if [[ $tech_added == false && ${#new_tech_entries[@]} -gt 0 ]]; then
                local e
                for e in "${new_tech_entries[@]}"; do echo "$e" >> "$temp_file"; done
                tech_added=true
            fi
            echo "$line" >> "$temp_file"
            in_tech_section=false
            continue
        fi
        if [[ $in_tech_section == true ]] && [[ -z "$line" ]]; then
            if [[ $tech_added == false && ${#new_tech_entries[@]} -gt 0 ]]; then
                local e
                for e in "${new_tech_entries[@]}"; do echo "$e" >> "$temp_file"; done
                tech_added=true
            fi
            echo "$line" >> "$temp_file"
            continue
        fi

        # --- Recent Changes handling ---------------------------------------
        if [[ "$line" == "## Recent Changes" ]]; then
            echo "$line" >> "$temp_file"
            [[ -n "$new_change_entry" ]] && echo "$new_change_entry" >> "$temp_file"
            in_changes_section=true
            continue
        elif [[ $in_changes_section == true ]] && [[ "$line" =~ ^##[[:space:]] ]]; then
            echo "$line" >> "$temp_file"
            in_changes_section=false
            continue
        elif [[ $in_changes_section == true ]] && [[ "$line" == "- "* ]]; then
            if [[ $existing_changes_count -lt 2 ]]; then
                echo "$line" >> "$temp_file"
                ((existing_changes_count++))
            fi
            continue
        fi

        # --- Last updated date ---------------------------------------------
        if [[ "$line" =~ \*\*Last\ updated\*\*:.*[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9] ]]; then
            echo "$line" | sed "s/[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]/$current_date/" >> "$temp_file"
        else
            echo "$line" >> "$temp_file"
        fi
    done < "$target_file"

    # Post-loop: if file ended while still in tech section, append any
    # remaining new entries.
    if [[ $in_tech_section == true && $tech_added == false && ${#new_tech_entries[@]} -gt 0 ]]; then
        local e
        for e in "${new_tech_entries[@]}"; do echo "$e" >> "$temp_file"; done
    fi

    # Sanity-check: warn (but do not fail) if any unsubstituted template
    # placeholders are detected. These typically indicate a stale create-time
    # substitution that the User should clean up manually.
    if grep -Eq '\[(ONLY COMMANDS FOR ACTIVE TECHNOLOGIES|EXTRACTED FROM ALL PLAN\.MD FILES|ACTUAL STRUCTURE FROM PLANS|LANGUAGE-SPECIFIC, ONLY FOR LANGUAGES IN USE|LAST 3 FEATURES AND WHAT THEY ADDED)\]' "$temp_file"; then
        log_warning "Unsubstituted template placeholders remain in $target_file. Review the file manually."
    fi

    mv "$temp_file" "$target_file" || { log_error "Failed to update"; rm -f "$temp_file"; return 1; }
    return 0
}

update_agent_file() {
    local target_file="$1"
    local agent_name="$2"
    log_info "Updating $agent_name context file: $target_file"
    local project_name=$(basename "$REPO_ROOT")
    local current_date=$(date +%Y-%m-%d)
    local target_dir=$(dirname "$target_file")
    [[ ! -d "$target_dir" ]] && mkdir -p "$target_dir"
    if [[ ! -f "$target_file" ]]; then
        local temp_file=$(mktemp)
        if create_new_agent_file "$target_file" "$temp_file" "$project_name" "$current_date"; then
            mv "$temp_file" "$target_file" && log_success "Created new $agent_name context file"
        else
            rm -f "$temp_file"; return 1
        fi
    else
        update_existing_agent_file "$target_file" "$current_date" && log_success "Updated $agent_name context file"
    fi
}

update_specific_agent() {
    local agent_type="$1"
    case "$agent_type" in
        claude) update_agent_file "$CLAUDE_FILE" "Claude Code" ;;
        gemini) update_agent_file "$GEMINI_FILE" "Gemini CLI" ;;
        copilot) update_agent_file "$COPILOT_FILE" "GitHub Copilot" ;;
        cursor-agent) update_agent_file "$CURSOR_FILE" "Cursor IDE" ;;
        qwen) update_agent_file "$QWEN_FILE" "Qwen Code" ;;
        opencode|codex) update_agent_file "$AGENTS_FILE" "Codex/opencode" ;;
        windsurf) update_agent_file "$WINDSURF_FILE" "Windsurf" ;;
        kilocode) update_agent_file "$KILOCODE_FILE" "Kilo Code" ;;
        auggie) update_agent_file "$AUGGIE_FILE" "Auggie CLI" ;;
        roo) update_agent_file "$ROO_FILE" "Roo Code" ;;
        codebuddy) update_agent_file "$CODEBUDDY_FILE" "CodeBuddy CLI" ;;
        qodercli) update_agent_file "$QODER_FILE" "Qoder CLI" ;;
        amp) update_agent_file "$AMP_FILE" "Amp" ;;
        shai) update_agent_file "$SHAI_FILE" "SHAI" ;;
        q) update_agent_file "$Q_FILE" "Amazon Q Developer CLI" ;;
        agy) update_agent_file "$AGY_FILE" "Antigravity" ;;
        bob) update_agent_file "$BOB_FILE" "IBM Bob" ;;
        *) log_error "Unknown agent type '$agent_type'"; exit 1 ;;
    esac
}

update_all_existing_agents() {
    local found_agent=false
    [[ -f "$CLAUDE_FILE" ]] && { update_agent_file "$CLAUDE_FILE" "Claude Code"; found_agent=true; }
    [[ -f "$GEMINI_FILE" ]] && { update_agent_file "$GEMINI_FILE" "Gemini CLI"; found_agent=true; }
    [[ -f "$COPILOT_FILE" ]] && { update_agent_file "$COPILOT_FILE" "GitHub Copilot"; found_agent=true; }
    [[ -f "$CURSOR_FILE" ]] && { update_agent_file "$CURSOR_FILE" "Cursor IDE"; found_agent=true; }
    [[ -f "$AGENTS_FILE" ]] && { update_agent_file "$AGENTS_FILE" "Codex/opencode"; found_agent=true; }
    [[ -f "$WINDSURF_FILE" ]] && { update_agent_file "$WINDSURF_FILE" "Windsurf"; found_agent=true; }
    [[ "$found_agent" == false ]] && { log_info "No agent files found, creating Claude..."; update_agent_file "$CLAUDE_FILE" "Claude Code"; }
}

main() {
    validate_environment
    log_info "=== Updating agent context files for feature $CURRENT_BRANCH ==="
    parse_plan_data "$NEW_PLAN" || { log_error "Failed to parse plan data"; exit 1; }
    if [[ -z "$AGENT_TYPE" ]]; then
        update_all_existing_agents
    else
        update_specific_agent "$AGENT_TYPE"
    fi
    log_success "Agent context update completed"
}

[[ "${BASH_SOURCE[0]}" == "${0}" ]] && main "$@"
