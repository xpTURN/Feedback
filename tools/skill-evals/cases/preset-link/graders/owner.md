---
type: regex
target: {source: file, path: Assets/Scenes/Arena.unity}
---

--- !u!1 &(\d+)\nGameObject:\n(?:  [^\n]*\n)*?  m_Name: Enemy\n[\s\S]*\n--- !u!114 &\d+\nMonoBehaviour:\n(?:  [^\n]*\n)*?  m_GameObject: \{fileID: \1\}
