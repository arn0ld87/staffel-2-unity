# Fortschrittslog

1. 2026-10-27: Tooling geprüft.
   - Unity CLI 1.0.0-beta.14 ist installiert, vorher weder Editor noch Login noch Lizenz.
   - Installiert wird Unity 6000.3.27f1 LTS mit `mac-il2cpp`, `linux-mono` und `linux-il2cpp` (Freigabe von Alex).
   - code-review-graph kennt `alexle135.de`, der Graph steht aber auf einem Branch 63 Commits hinter `origin/main`. Deshalb wurde ein Referenz-Worktree `main-ref` angelegt und das Inventar per ctx erstellt.
2. 2026-10-27: Inventar Staffel 2 erstellt: E0–E3, Datenmodell, Agent-Protokoll, Spielstand.
3. 2026-10-27: Runtime-Spike bestanden auf macOS (Node, Bun-Binary) und auf Linux x86_64 (gns3). Siehe `runtime-spike.md`.
