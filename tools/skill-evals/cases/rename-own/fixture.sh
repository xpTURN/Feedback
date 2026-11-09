#!/usr/bin/env bash
# the shared fixture project into the run's workspace, then a feedback of the project's own that a scene's player holds
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
bash "${here}/../../fixture/scaffold.sh"
cp -R "${here}/../../fixture/rename-own/." .
ls Assets/Scripts/HitSound.cs Assets/Scenes/Range.unity >/dev/null
