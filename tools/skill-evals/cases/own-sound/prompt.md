---
name: own-sound
max_turns: 25
timeout_seconds: 600
allowed_tools: [Read, Glob, Grep, Skill]
---

Add a hit sound from our FMOD Studio project to the feedback package this project uses, as a feedback I can add to a Feedback Player's list: it plays the FMOD event once at the hit's position, louder for a stronger hit. Write it in Assets/Scripts/HitSound.cs.
