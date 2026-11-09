---
name: play-from-code
max_turns: 25
timeout_seconds: 600
allowed_tools: [Read, Glob, Grep, Skill]
---

Edit Assets/Scripts/Enemy.cs so that TakeDamage plays the enemy's hit feedback with the feedback package this project uses, at a strength of damage / maxHealth. The player component will be assigned in the inspector.
