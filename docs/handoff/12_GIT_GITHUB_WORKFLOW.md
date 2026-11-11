# GitHub repository creation and PR workflow

Default new repository name: `webwild`, private under the `gh`-authenticated user. The coding agent **must** create it using GitHub CLI; the archive creator has not done this.

## Preflight / safe repo naming
```bash
gh --version
gh auth status
OWNER="$(gh api user --jq .login)"
if gh repo view "$OWNER/webwild" --json name >/dev/null 2>&1; then
  echo "webwild exists; do not overwrite. Try webwild-game after checking too."
fi
```

## P0: initialise local and create on GitHub
```bash
mkdir webwild && cd webwild
# Add Godot project, .gitignore, READMEs, AGENTS.md and initial C# solution here first.
git init -b main
git add .
git commit -m "chore: bootstrap Godot .NET Webwild project"
gh repo create webwild --private --source=. --remote=origin --push \
  --description "Webwild: child-friendly spider colony simulator in Godot and C#"
gh repo view --json nameWithOwner,url,visibility
git status --short --branch
```

If the default account/name is unavailable, **verify** alternate name before using it, avoid any destructive command, and document the choice. If `gh` needs sign-in, stop only remote-dependent operations; continue local implementation and describe exactly what credential action is required.

## P1–P8 branches
```bash
git switch main && git pull --ff-only
git switch -c p1-garden-movement
# implement, build, test, commit
git push -u origin p1-garden-movement
gh pr create --base main --head p1-garden-movement \
  --title "P1: Spider movement and garden clearing" \
  --body-file docs/prs/P1.md
# After checks are green and reviewed:
gh pr checks
gh pr merge --squash --delete-branch
```

Adapt branch and title for each phase. `gh pr checks` can report pending/failure; do not merge merely because it was invoked. If CI is unavailable, run and record local suite before merge; do not claim automated checks passed.

## Git quality
- Commit focused changes and evidence; never push secrets or local saves.
- Add `.godot/`, `.mono/`, `bin/`, `obj/`, testresults, exports, user settings, backups to `.gitignore` as appropriate. Keep `.tscn`/`.tres`, source models and assets tracked.
- `main` must be clean, green and playable after each merge. Do not rewrite commits on main or tag a broken version.
- PR summary includes actual command output and state, not just "tests pass".
- Prefer `gh pr view --json state,mergeable,statusCheckRollup,url` and `git rev-parse HEAD` for final evidence.

## Final
Tag `v0.1.0` at verified main HEAD; push tag; optionally `gh release create v0.1.0 ...` if release artifacts exist and user credentials allow. Final response must include both repo URL and precise tag/commit, or clear failed reason if missing.
