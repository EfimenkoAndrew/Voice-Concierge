## Summary

<!-- One or two sentences on what changed and why. -->

## Type
- [ ] feat
- [ ] fix
- [ ] refactor
- [ ] docs
- [ ] chore / infra

## Test plan
<!-- How to verify. Paste the curl / UI steps reviewers should reproduce. -->

## Checklist
- [ ] `dotnet test` passes locally
- [ ] `npm run check-all` passes (if UI touched)
- [ ] No secrets in the diff (`.env` / API keys / certs)
- [ ] `docker compose up --build -d` still works end-to-end (if compose / Dockerfiles touched)
- [ ] CHANGELOG / docs updated if behavior changed
