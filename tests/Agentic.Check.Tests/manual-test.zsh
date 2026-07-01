#!/usr/bin/env zsh
set -euo pipefail

script_path=${(%):-%x}
script_dir=${script_path:A:h}
repo_root=${script_dir:h:h}
tool_project="$repo_root/src/Agentic.Check/Agentic.Check.csproj"

no_gh=false
case "${1:-}" in
    --no-gh)
        no_gh=true
        shift
        ;;
    "")
        ;;
    *)
        print -u2 -r -- "Usage: ${0:t} [--no-gh]"
        exit 2
        ;;
esac

if (( $# > 0 )); then
    print -u2 -r -- "Usage: ${0:t} [--no-gh]"
    exit 2
fi

if $no_gh; then
    path_setup='export PATH="/usr/local/share/dotnet:$HOME/.dotnet/tools:/usr/bin:/bin:/usr/sbin:/sbin"'
else
    path_setup='export PATH="/usr/local/share/dotnet:/usr/local/bin:/opt/homebrew/bin:$PATH"'
fi

terminal_command="cd ${(q)repo_root}; $path_setup; agentic-check() { dotnet run --project ${(q)tool_project} -- \"\$@\"; }; printf \"Manual test shell ready. Try: agentic-check --help\\n\"; type agentic-check"

if [[ "$(uname -s)" != "Darwin" ]] || ! command -v osascript >/dev/null 2>&1; then
    print -r -- "This launcher opens a macOS Terminal window."
    print -r -- "Run this setup command in a shell instead:"
    print -r -- "$terminal_command"
    exit 0
fi

osascript - "$terminal_command" <<'APPLESCRIPT'
on run argv
    tell application "Terminal"
        activate
        do script item 1 of argv
    end tell
end run
APPLESCRIPT
