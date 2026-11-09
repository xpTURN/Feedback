---
type: llm
---

Ground truth from the package's documentation: add a Feedback UI Trigger (Component > xpTURN > Feedback > Feedback UI Trigger) to the Button itself and give it two entries, Pointer Enter with Play Forward and Pointer Exit with Play Backward, playing a Feedback Player on the button whose list grows its scale (for example a Transform/Scale item, or the Button Hover preset); a play that comes back the other way turns around where it is, with no jump. PASS if the answer uses the package's Feedback UI Trigger with a Pointer Enter entry and a Pointer Exit entry on the button and a Feedback Player that scales it, with no script. FAIL if it needs a script, or uses something other than the Feedback UI Trigger for the pointer events.
