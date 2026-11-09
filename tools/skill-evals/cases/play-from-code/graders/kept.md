---
type: regex
target: {source: file, path: Assets/Scripts/Enemy.cs}
---

health -= damage[\s\S]*Destroy\(gameObject\)
