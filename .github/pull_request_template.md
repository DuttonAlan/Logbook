## What and why

<!-- What does this change, and why? Link the issue: "Closes #123". -->

## How was it tested?

<!-- New or changed tests, manual checks. -->

## Checklist

- [ ] `dotnet build Logbook.slnx` and `dotnet test --solution Logbook.slnx` pass
- [ ] `npm run lint && npm run build` pass in `src/Logbook.Web`, if the web interface changed
- [ ] Tests cover the change
- [ ] Model changes come with a migration
- [ ] SDK changes never throw into or block the host application; the failure path is tested
- [ ] No API Keys or other secrets are logged
- [ ] Documentation in `docs/` is updated if behavior, configuration or endpoints changed
- [ ] `CHANGELOG.md` has an entry under *Unreleased*

<!-- Security vulnerabilities must not be fixed in a public pull request first. See SECURITY.md. -->
