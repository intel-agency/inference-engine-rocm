### Summary

<!-- What does this PR change, and why? Reference the issue it closes if any. -->

### Couplet impact

<!-- Does this change the ORT + ROCm couplet, native build, or package version scheme?
     If yes, describe the change and link to plan_docs/Multi-Version-Branching-Strategy.md discussion. -->

- [ ] No couplet / native-build / packaging change
- [ ] Couplet or packaging change (details above)

### Validation performed

<!-- Tier-1 suite (dotnet test) results, local pack result, CI run link. Note that the
     Tier-1 suite requires CI-built .so files in runtimes/linux-x64/native/. -->

- [ ] `dotnet build inference-engine-rocm.slnx` green
- [ ] Tier-1 validation suite green
- [ ] Commits are GPG-signed; no squash/rebase history
