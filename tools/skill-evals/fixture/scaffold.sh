#!/usr/bin/env bash
# fills a run's empty workspace with the fixture project — a Unity project's files (no editor) with com.xpturn.feedback
# installed from git: its copy in Library/PackageCache is put beside this script by tools/check-skill.sh --eval
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cp -R "${here}/project/." .
ls Library/PackageCache/com.xpturn.feedback@*/package.json >/dev/null
