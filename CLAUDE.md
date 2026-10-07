# Repository rules

These are hard rules. Follow them for every change, commit and push.

## No real Agile PM data in the repository

- Everything in the `AgilePM` block of `AutoPM_MCP/appsettings.json` must be dummy data when committed:
  `ApiBaseUrl` is `https://agilepm.example.com/api/`, and `Email` and `Password` are empty.
- Real endpoints and credentials go in `AutoPM_MCP/appsettings.Local.json` (git-ignored), or environment variables.
- Documentation, code comments, examples and commit messages never contain real Agile PM URLs, hosts, emails
  or passwords. Use dummy data such as `https://agilepm.example.com`, `<your-agile-pm-host>` and `user@example.com`.
- Before committing, check the staged diff for real hosts or credentials.

## No AI attribution

- Never mention Claude, Anthropic or any AI assistant as an author: no `Co-Authored-By` trailers,
  no "Generated with" lines in commits, pull requests, code comments or docs.
- Naming AI clients as products the server works with (for example, setup steps for Claude Code) is documentation, not attribution, and is allowed.
