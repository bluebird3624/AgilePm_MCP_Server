# AutoPM MCP Server

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io/) server that lets AI assistants such as Claude,
GitHub Copilot and other MCP clients work with **Agile PM** for a signed-in user: browse their projects, plan epics
and user stories, and create, update and move tasks.

- Works over **HTTP** or **stdio** (also called *bus mode*).
- Uses **the caller's own Agile PM bearer token** for every request, so users only see and change what Agile PM already allows them to.
- Ships as **one self-contained executable** for Windows, Linux or macOS. The target machine doesn't need .NET installed. See [Building a standalone executable](#building-a-standalone-executable) and [Publishing and hosting on a server](#publishing-and-hosting-on-a-server).

---

## Contents

1. [How it works](#how-it-works)
2. [Requirements](#requirements)
3. [Quick start](#quick-start)
4. [Configuration](#configuration)
5. [Transport modes](#transport-modes)
6. [Authentication](#authentication)
7. [Connecting an MCP client](#connecting-an-mcp-client)
8. [Tool reference](#tool-reference)
9. [Enum reference](#enum-reference)
10. [Behaviour you should know about](#behaviour-you-should-know-about)
11. [Building a standalone executable](#building-a-standalone-executable)
12. [Publishing and hosting on a server](#publishing-and-hosting-on-a-server)
13. [Logging](#logging)
14. [Project structure](#project-structure)
15. [Adding a new tool](#adding-a-new-tool)
16. [Testing](#testing)
17. [Troubleshooting](#troubleshooting)
18. [Security notes](#security-notes)
19. [Known limitations and roadmap](#known-limitations-and-roadmap)

---

## How it works

```
┌──────────────┐   MCP (HTTP or stdio)     ┌──────────────────┐   HTTPS + same bearer token   ┌──────────────┐
│  MCP client  │ ────────────────────────► │  AutoPM MCP      │ ────────────────────────────► │  Agile PM    │
│ (Claude, VS  │   Authorization: Bearer   │  Server          │   Authorization: Bearer       │  Core API    │
│  Code, ...)  │   <token>  / AGILEPM_TOKEN│  (this project)  │   <token>                     │  /api/...    │
└──────────────┘ ◄──────────────────────── └──────────────────┘ ◄──────────────────────────── └──────────────┘
                   tool results (JSON)                            Result / PaginatedResult JSON
```

1. The MCP client calls a tool, for example `list_my_projects`.
2. The server finds the caller's token. In HTTP mode it reads the `Authorization` header of that MCP request. In stdio mode it reads the `AGILEPM_TOKEN` environment variable.
3. `AgilePmClient` sends the request to Agile PM with **the same token**. The server never issues, stores or caches tokens.
4. The Agile PM response envelope is unwrapped and errors are turned into plain messages. The tool returns compact JSON to the AI.

The HTTP transport is **stateless**. Each request stands alone, so you can run several instances behind a load balancer with no sticky sessions.

Agile PM API documentation (Swagger): <https://your-agilepm-host/coreswagger/index.html>

---

## Requirements

| To... | You need |
|-------|----------|
| Run a published build | Nothing: the executable contains the .NET runtime |
| Build from source | [.NET 10 SDK](https://dotnet.microsoft.com/download) |
| Use the tools | An Agile PM account, and network access to the Agile PM API |

---

## Quick start

```bash
# 1. Run from source in HTTP mode (listens on http://localhost:6015)
dotnet run --launch-profile http

# 2. Sign in to get a token (or use the login tool from your MCP client)
#    See AutoPM_MCP.http for ready-made requests.

# 3. Point your MCP client at http://localhost:6015 with the header
#    Authorization: Bearer <access token>
```

To run in stdio mode instead:

```bash
dotnet run -- --stdio            # token from the AGILEPM_TOKEN environment variable
```

---

## Configuration

Settings are read in this order, **later sources win**:

1. `appsettings.json` (next to the executable)
2. `appsettings.{Environment}.json` (e.g. `appsettings.Development.json`)
3. User secrets (Development only)
4. Environment variables
5. Command-line arguments

### Settings

| Key | Env variable form | Default | Description |
|-----|-------------------|---------|-------------|
| `AgilePM:ApiBaseUrl` | `AgilePM__ApiBaseUrl` | `https://your-agilepm-host/api/` | Base URL of the Agile PM API. **Required.** Must be absolute; the trailing `/` is added if missing. |
| `AgilePM:TimeoutSeconds` | `AgilePM__TimeoutSeconds` | `30` | Timeout for each Agile PM request (5–300). |
| `McpTransport` | `McpTransport` | `Http` | `Http` or `Stdio`. The `--stdio` flag overrides it. |
| `AGILEPM_TOKEN` | `AGILEPM_TOKEN` | – | The user's access token. **Used in stdio mode only.** |
| `Urls` | `ASPNETCORE_URLS` | `http://localhost:5000` | Address and port the HTTP server listens on (published builds). |
| `Serilog:*` | `Serilog__...` | see `appsettings.json` | Logging configuration (see [Logging](#logging)). |

> Environment variables use a double underscore (`__`) in place of `:`. For example, `AgilePM__ApiBaseUrl`.

### Example `appsettings.json` (excerpt)

```json
{
  "McpTransport": "Http",
  "AgilePM": {
    "ApiBaseUrl": "https://your-agilepm-host/api/",
    "TimeoutSeconds": 30
  }
}
```

The server **validates its settings at startup**. If `ApiBaseUrl` is missing or invalid, it prints the reason to stderr and exits with code `1`.

---

## Transport modes

| | HTTP | stdio ("bus mode") |
|---|---|---|
| Start | `AutoPM_MCP` (default) or `McpTransport=Http` | `AutoPM_MCP --stdio` or `McpTransport=Stdio` |
| Who starts the process | You (or a service manager) | The MCP client starts it as a child process |
| Users per process | Many: each request carries its own token | One: the token in `AGILEPM_TOKEN` |
| Token source | `Authorization: Bearer <token>` header on each MCP request | `AGILEPM_TOKEN` environment variable |
| Endpoint | `POST /` (Streamable HTTP, stateless) | stdin/stdout |
| Logs | Console and file | **stderr** and file (stdout is reserved for the protocol) |
| Typical use | Shared or team server, remote clients | Desktop clients on one user's machine |

In stdio mode the server loads `appsettings.json` from **the executable's folder**, not the current working directory. That matters because MCP clients often start servers from an arbitrary directory.

---

## Authentication

Agile PM uses JWT bearer tokens. This server is a pass-through: it **never stores tokens**. The user gets a token and configures it in their MCP client.

### Typical flow

1. **Get a token.** Use the `login` tool (email + password). It returns the Agile PM response, including the access token, refresh token and expiry.
   - `login` and `refresh_token` are the only tools that work without a token.
2. **Configure the token** in your MCP client:
   - HTTP: add the header `Authorization: Bearer <access token>`.
   - stdio: set the environment variable `AGILEPM_TOKEN=<access token>`.
3. **Use the tools.** Every call forwards the token to Agile PM.
4. **When the token expires**, tools return *"Agile PM rejected the access token (401) … call refresh_token or login"*. Call `refresh_token` with the old access token and the refresh token, then update the configured token.

> The AI client can't change its own HTTP headers or environment, so step 2 is always done by the person who configures the client.

---

## Connecting an MCP client

Any client that supports MCP can use this server. Registering it means telling the client **how to reach the server** (a URL or a command to start) and **which Agile PM token to send**.

### Step 1: choose how the client connects

| | **HTTP** (remote or local server) | **stdio** (client starts the exe) |
|---|---|---|
| Use when | The server is [hosted](#publishing-and-hosting-on-a-server) for a team, or you run it yourself with `dotnet run` | Each user has the executable on their own machine |
| The client needs | The server URL + the header `Authorization: Bearer <token>` | The exe path, the argument `--stdio`, and the env variable `AGILEPM_TOKEN=<token>` |
| Server must be running first | Yes | No: the client starts and stops it |

### Step 2: get a token

Call the `login` tool (see [Authentication](#authentication)) or use the `login` request in `AutoPM_MCP.http`. Copy the access token from the response.

> **Bootstrapping:** `login` works without a token. You can register the server with any placeholder token, call `login` from the client, then replace the placeholder with the real token.

### Placeholders used below

| Placeholder | Replace with | Example |
|-------------|-------------|---------|
| `<URL>` | The MCP endpoint (the server root) | `http://localhost:6015` locally, or `https://autopm-mcp.example.com/` when hosted |
| `<EXE>` | Full path to the executable | Windows `C:\Tools\AutoPM_MCP\AutoPM_MCP.exe`, Linux/macOS `/opt/autopm-mcp/AutoPM_MCP` |
| `<token>` | Your Agile PM access token | `eyJhbGciOi...` |

> In **JSON**, Windows backslashes must be doubled: `"C:\\Tools\\AutoPM_MCP\\AutoPM_MCP.exe"`.
> In **TOML** single-quoted strings and in **YAML**, single backslashes are fine.

### Client support at a glance

| Client | HTTP + custom header | stdio | Keeps the token out of the file | Add from CLI |
|--------|:---:|:---:|---|---|
| [Claude Code](#claude-code) | ✅ | ✅ | `${VAR}` env expansion | `claude mcp add` |
| [Claude Desktop](#claude-desktop) | ⚠️ beta / via bridge | ✅ | – | – |
| [VS Code (GitHub Copilot)](#vs-code-github-copilot) | ✅ | ✅ | `${input:...}` password prompt | `code --add-mcp` |
| [Visual Studio 2022 17.14+ / 2026](#visual-studio) | ✅ | ✅ | `${input:...}` password prompt | – |
| [Cursor](#cursor) | ✅ | ✅ | `${env:VAR}` | – (deeplink) |
| [Windsurf / Devin Desktop](#windsurf--devin-desktop) | ✅ | ✅ | `${env:VAR}` | – |
| [Cline](#cline) | ✅ | ✅ | – | – |
| [Continue](#continue) | ✅ | ✅ | `${{ secrets.NAME }}` | – |
| [Zed](#zed) | ✅ | ✅ | – | – |
| [JetBrains AI Assistant / Junie](#jetbrains-ai-assistant-and-junie) | ✅ (Junie) | ✅ | – | Junie: `/mcp` |
| [OpenAI Codex CLI](#openai-codex-cli) | ✅ | ✅ | `bearer_token_env_var` | `codex mcp add` |
| [Gemini CLI](#gemini-cli) | ✅ | ✅ | `$VAR` in `env` | `gemini mcp add` |
| [MCP Inspector](#mcp-inspector-testing) | ✅ | ✅ | – | `npx` |

> **Config formats change between client versions.** The snippets below were checked against each vendor's documentation in October 2026. If one doesn't work, check the linked docs for your version.

---

### Claude Code

Docs: <https://docs.claude.com/en/docs/claude-code/mcp>

**From the command line:**

```bash
# HTTP
claude mcp add --transport http autopm <URL> --header "Authorization: Bearer <token>"

# stdio
claude mcp add autopm --env AGILEPM_TOKEN=<token> -- "<EXE>" --stdio
```

Add `--scope` to choose where it's saved:

| Scope | Saved in | Who gets it |
|-------|----------|-------------|
| `local` (default) | Your user config, for this project only | Just you, in this project |
| `project` | `.mcp.json` in the repo root | Everyone who clones the repo |
| `user` | Your user config | Just you, in every project |

**Shared with the team through `.mcp.json`.** Commit this file and keep the token out of it with `${VAR}` expansion. Each developer sets `AGILEPM_TOKEN` in their own environment:

```json
{
  "mcpServers": {
    "autopm": {
      "type": "http",
      "url": "https://autopm-mcp.example.com/",
      "headers": { "Authorization": "Bearer ${AGILEPM_TOKEN}" }
    }
  }
}
```

stdio variant:

```json
{
  "mcpServers": {
    "autopm": {
      "type": "stdio",
      "command": "C:\\Tools\\AutoPM_MCP\\AutoPM_MCP.exe",
      "args": ["--stdio"],
      "env": { "AGILEPM_TOKEN": "${AGILEPM_TOKEN}" }
    }
  }
}
```

**Check it:** `claude mcp list` from the terminal, or `/mcp` inside Claude Code.

---

### Claude Desktop

Docs: <https://modelcontextprotocol.io/quickstart/user> and <https://claude.com/docs/connectors/custom/remote-mcp>

Edit `claude_desktop_config.json` (Settings → Developer → *Edit Config*):

| OS | Location |
|----|----------|
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |

**stdio (recommended for Claude Desktop):**

```json
{
  "mcpServers": {
    "autopm": {
      "command": "C:\\Tools\\AutoPM_MCP\\AutoPM_MCP.exe",
      "args": ["--stdio"],
      "env": { "AGILEPM_TOKEN": "<token>" }
    }
  }
}
```

**HTTP:** there are two ways.

1. **Through the `mcp-remote` bridge (works everywhere, needs Node.js).** The bridge runs locally, so `localhost` and private-network URLs work too:

   ```json
   {
     "mcpServers": {
       "autopm": {
         "command": "npx",
         "args": ["-y", "mcp-remote", "<URL>", "--header", "Authorization:${AUTH_HEADER}", "--transport", "http-only"],
         "env": { "AUTH_HEADER": "Bearer <token>" }
       }
     }
   }
   ```

   Write `Authorization:${AUTH_HEADER}` exactly as shown, with no space after the colon, and keep the token in `env`. Claude Desktop on Windows doesn't handle spaces inside `args` correctly.

2. **Custom connector (Settings → Connectors → *Add custom connector*).** Use this only if your organisation has the **Request headers** beta:
   - Choose **No sign-in**, then add the header `Authorization` with value `Bearer <token>`.
   - Connectors connect **from Anthropic's cloud**, so `<URL>` must be a **public HTTPS** address. `localhost` won't work.
   - If the header is ignored and an OAuth sign-in starts, use option 1.

**Restart Claude Desktop completely** after editing the config: quit it from the system tray or menu bar, not just the window. The tools appear under the tools (🔨) icon in the chat box.

---

### VS Code (GitHub Copilot)

Docs: <https://code.visualstudio.com/docs/copilot/chat/mcp-servers>

| Scope | File |
|-------|------|
| Workspace (commit to share) | `.vscode/mcp.json` |
| User (all workspaces) | Command Palette → **MCP: Open User Configuration** |

The `inputs` block prompts for the token once and stores it securely, so it's never written to the file:

```json
{
  "inputs": [
    { "id": "agilePmToken", "type": "promptString", "description": "Agile PM access token", "password": true }
  ],
  "servers": {
    "autopm": {
      "type": "http",
      "url": "<URL>",
      "headers": { "Authorization": "Bearer ${input:agilePmToken}" }
    }
  }
}
```

stdio variant:

```json
{
  "inputs": [
    { "id": "agilePmToken", "type": "promptString", "description": "Agile PM access token", "password": true }
  ],
  "servers": {
    "autopm": {
      "type": "stdio",
      "command": "C:\\Tools\\AutoPM_MCP\\AutoPM_MCP.exe",
      "args": ["--stdio"],
      "env": { "AGILEPM_TOKEN": "${input:agilePmToken}" }
    }
  }
}
```

**From the command line:**

```bash
code --add-mcp "{\"name\":\"autopm\",\"type\":\"http\",\"url\":\"<URL>\",\"headers\":{\"Authorization\":\"Bearer <token>\"}}"
```

**Use it:** open Copilot Chat, switch to **Agent** mode and check the server's tools are enabled in the tools picker. **MCP: List Servers** shows the status, with Start/Stop/Restart and the server's output log.

> VS Code warns about `http://localhost:6015` vs `https://localhost:5062`. Use the `http` address; see [Troubleshooting](#troubleshooting).

---

### Visual Studio

Docs: <https://learn.microsoft.com/visualstudio/ide/mcp-servers>. Requires Visual Studio 2022 **17.14 or later**, or Visual Studio 2026, with GitHub Copilot.

Visual Studio reads these files, in order:

| File | Use |
|------|-----|
| `%USERPROFILE%\.mcp.json` | Global, for all your solutions |
| `<SolutionDir>\.vs\mcp.json` | Just you, this solution (not committed) |
| `<SolutionDir>\.mcp.json` | This solution; commit it to share |
| `<SolutionDir>\.vscode\mcp.json` | Shared with VS Code |
| `<SolutionDir>\.cursor\mcp.json` | Shared with Cursor |

The format is the same as VS Code: the top-level key is **`servers`**, not `mcpServers`.

```json
{
  "inputs": [
    { "id": "agilePmToken", "type": "promptString", "description": "Agile PM access token", "password": true }
  ],
  "servers": {
    "autopm": {
      "type": "http",
      "url": "<URL>",
      "headers": { "Authorization": "Bearer ${input:agilePmToken}" }
    }
  }
}
```

You can also add the server from the UI: Copilot Chat → **Agent** mode → **Tools** → **+** → *Add custom MCP server*.

---

### Cursor

Docs: <https://cursor.com/docs/context/mcp>

| Scope | File |
|-------|------|
| Global | `~/.cursor/mcp.json` (Windows: `%USERPROFILE%\.cursor\mcp.json`) |
| Project | `.cursor/mcp.json` |

`${env:NAME}` reads environment variables, so the token can stay out of the file:

```json
{
  "mcpServers": {
    "autopm": {
      "url": "<URL>",
      "headers": { "Authorization": "Bearer ${env:AGILEPM_TOKEN}" }
    }
  }
}
```

stdio variant:

```json
{
  "mcpServers": {
    "autopm": {
      "type": "stdio",
      "command": "C:\\Tools\\AutoPM_MCP\\AutoPM_MCP.exe",
      "args": ["--stdio"],
      "env": { "AGILEPM_TOKEN": "${env:AGILEPM_TOKEN}" }
    }
  }
}
```

**One-click install link** for your team (from Cursor's install-links docs). Base64-encode the *single server object* (not wrapped in `mcpServers`):

```
cursor://anysphere.cursor-deeplink/mcp/install?name=autopm&config=<base64 of {"url":"<URL>","headers":{"Authorization":"Bearer ${env:AGILEPM_TOKEN}"}}>
```

Check the server under Cursor Settings → **MCP**, where a green dot means connected. The Cursor CLI (`agent mcp list`) reads the same files but can't add servers.

---

### Windsurf / Devin Desktop

Docs: <https://docs.devin.ai/desktop/cascade/mcp> (Windsurf's docs now redirect there).

| Build | Config file |
|-------|-------------|
| Current (Devin Desktop) | macOS/Linux `~/.config/devin/mcp_config.json`; Windows `%APPDATA%\devin\mcp_config.json` |
| Older Windsurf builds | `~/.codeium/windsurf/mcp_config.json` (Windows `%USERPROFILE%\.codeium\windsurf\mcp_config.json`) |

Remote servers use **`serverUrl`** and have no `type` field:

```json
{
  "mcpServers": {
    "autopm": {
      "serverUrl": "<URL>",
      "headers": { "Authorization": "Bearer ${env:AGILEPM_TOKEN}" }
    }
  }
}
```

stdio variant:

```json
{
  "mcpServers": {
    "autopm": {
      "command": "C:\\Tools\\AutoPM_MCP\\AutoPM_MCP.exe",
      "args": ["--stdio"],
      "env": { "AGILEPM_TOKEN": "<token>" }
    }
  }
}
```

After saving, refresh the MCP list in the Cascade panel.

---

### Cline

Docs: <https://docs.cline.bot/mcp/configuring-mcp-servers>

Open the Cline panel → **MCP Servers** icon → **Configure** to edit `cline_mcp_settings.json` (the Cline CLI uses `~/.cline/mcp.json`). For this server's transport, use `"type": "streamableHttp"`:

```json
{
  "mcpServers": {
    "autopm": {
      "type": "streamableHttp",
      "url": "<URL>",
      "headers": { "Authorization": "Bearer <token>" },
      "disabled": false,
      "autoApprove": []
    }
  }
}
```

stdio variant:

```json
{
  "mcpServers": {
    "autopm": {
      "command": "C:\\Tools\\AutoPM_MCP\\AutoPM_MCP.exe",
      "args": ["--stdio"],
      "env": { "AGILEPM_TOKEN": "<token>" },
      "disabled": false,
      "autoApprove": []
    }
  }
}
```

`autoApprove` can list read-only tools (e.g. `"list_my_projects"`, `"get_my_work"`) so Cline doesn't ask before each call. Keep write tools out of it.

---

### Continue

Docs: <https://docs.continue.dev/customize/deep-dives/mcp>

Add the server to `~/.continue/config.yaml`, or as a block file in `.continue/mcpServers/autopm.yaml`. Store the token as a Continue secret and reference it with `${{ secrets.AGILEPM_TOKEN }}`:

```yaml
name: AutoPM
version: 0.0.1
schema: v1
mcpServers:
  - name: autopm
    type: streamable-http
    url: <URL>
    requestOptions:
      headers:
        Authorization: Bearer ${{ secrets.AGILEPM_TOKEN }}
```

stdio variant:

```yaml
mcpServers:
  - name: autopm
    type: stdio
    command: C:\Tools\AutoPM_MCP\AutoPM_MCP.exe
    args: ["--stdio"]
    env:
      AGILEPM_TOKEN: ${{ secrets.AGILEPM_TOKEN }}
```

MCP tools are only available in Continue's **Agent** mode.

---

### Zed

Docs: <https://zed.dev/docs/ai/mcp>

Add a `context_servers` entry to your Zed settings (Command Palette → `zed: open settings file`, or Settings → AI → MCP Servers):

```json
{
  "context_servers": {
    "autopm": {
      "url": "<URL>",
      "headers": { "Authorization": "Bearer <token>" }
    }
  }
}
```

stdio variant:

```json
{
  "context_servers": {
    "autopm": {
      "command": "C:\\Tools\\AutoPM_MCP\\AutoPM_MCP.exe",
      "args": ["--stdio"],
      "env": { "AGILEPM_TOKEN": "<token>" }
    }
  }
}
```

If no `Authorization` header is set, Zed tries an OAuth sign-in, which this server doesn't offer. Always set the header.

---

### JetBrains AI Assistant and Junie

Docs: <https://www.jetbrains.com/help/ai-assistant/mcp.html> and <https://junie.jetbrains.com/docs/junie-cli-mcp-configuration.html>

**AI Assistant** (IntelliJ IDEA, Rider, PyCharm, WebStorm, ...): **Settings → Tools → AI Assistant → Model Context Protocol (MCP)** → **Add**, then paste JSON. Choose *Global* or *Project* level.

```json
{
  "mcpServers": {
    "autopm": {
      "command": "C:\\Tools\\AutoPM_MCP\\AutoPM_MCP.exe",
      "args": ["--stdio"],
      "env": { "AGILEPM_TOKEN": "<token>" }
    }
  }
}
```

AI Assistant's documentation shows HTTP servers only as `{"url": "..."}` and doesn't mention custom headers. **Prefer stdio in AI Assistant.**

**Junie** (IDE plugin and CLI): `.junie/mcp/mcp.json` in the project, or `~/.junie/mcp/mcp.json` for all projects. HTTP with headers is supported:

```json
{
  "mcpServers": {
    "autopm": {
      "url": "<URL>",
      "headers": { "Authorization": "Bearer <token>" }
    }
  }
}
```

---

### OpenAI Codex CLI

Docs: <https://developers.openai.com/codex/mcp>. The CLI, IDE extension and desktop app share `~/.codex/config.toml` (or `.codex/config.toml` in a trusted project).

**From the command line:**

```bash
# HTTP: the token is read from the AGILEPM_TOKEN environment variable and sent as "Authorization: Bearer ..."
codex mcp add autopm --url <URL> --bearer-token-env-var AGILEPM_TOKEN

# stdio
codex mcp add autopm --env AGILEPM_TOKEN=<token> -- "<EXE>" --stdio
```

**Or edit `config.toml`:**

```toml
[mcp_servers.autopm]
url = "<URL>"
bearer_token_env_var = "AGILEPM_TOKEN"
tool_timeout_sec = 60
```

stdio variant:

```toml
[mcp_servers.autopm]
command = 'C:\Tools\AutoPM_MCP\AutoPM_MCP.exe'
args = ["--stdio"]
env_vars = ["AGILEPM_TOKEN"]          # forward AGILEPM_TOKEN from your shell...
# env = { AGILEPM_TOKEN = "<token>" } # ...or set it here
```

**Check it:** run `codex mcp list`, or `/mcp` inside Codex.

---

### Gemini CLI

Docs: <https://geminicli.com/docs/tools/mcp-server/>

Config file: `~/.gemini/settings.json` (user) or `.gemini/settings.json` (project; it wins).

> Gemini CLI picks the transport from the key name: **`httpUrl` = Streamable HTTP** (what this server uses), `url` = SSE (wrong for this server), `command` = stdio.

**From the command line:**

```bash
# HTTP
gemini mcp add -s user -t http -H "Authorization: Bearer <token>" autopm <URL>

# stdio
gemini mcp add -s user -e AGILEPM_TOKEN=<token> autopm "<EXE>" --stdio
```

**Or edit `settings.json`:**

```json
{
  "mcpServers": {
    "autopm": {
      "httpUrl": "<URL>",
      "headers": { "Authorization": "Bearer <token>" },
      "timeout": 60000
    }
  }
}
```

stdio variant (`$AGILEPM_TOKEN` is expanded from your shell environment):

```json
{
  "mcpServers": {
    "autopm": {
      "command": "C:\\Tools\\AutoPM_MCP\\AutoPM_MCP.exe",
      "args": ["--stdio"],
      "env": { "AGILEPM_TOKEN": "$AGILEPM_TOKEN" }
    }
  }
}
```

**Check it:** `/mcp` inside Gemini CLI lists the servers and their tools.

---

### MCP Inspector (testing)

The official debugging UI. It's useful for checking the server before you set up any AI client.

```bash
npx @modelcontextprotocol/inspector
```

In the browser page that opens:
- **HTTP:** Transport **Streamable HTTP**, URL `<URL>`. Under *Authentication*, set the header `Authorization` = `Bearer <token>`. Click **Connect**.
- **stdio:** Transport **STDIO**, command `<EXE>`, arguments `--stdio`, environment `AGILEPM_TOKEN=<token>`. Click **Connect**.

Then open **Tools** → **List Tools** and run `get_my_work` to confirm the token works end to end.

---

### Any other MCP client

Look for the client's "MCP servers" setting and fill in:

| Field | HTTP | stdio |
|-------|------|-------|
| Transport / type | *Streamable HTTP* (often `http`, `streamable-http` or `streamableHttp`). **Not** SSE. | `stdio` |
| URL | `<URL>` | – |
| Headers | `Authorization: Bearer <token>` | – |
| Command | – | `<EXE>` |
| Arguments | – | `--stdio` |
| Environment | – | `AGILEPM_TOKEN=<token>` |
| OAuth / sign-in | **None**: this server uses a static bearer token | – |

If a client supports only SSE, or remote servers without custom headers, use stdio. Or put the [`mcp-remote`](https://github.com/geelen/mcp-remote) bridge in front, as shown for [Claude Desktop](#claude-desktop).

---

### Check that it works

Ask the assistant something like:

> *"Which Agile PM projects am I on?"* → it should call `list_my_projects`.
> *"What's on my plate this week?"* → it should call `get_my_work`.

| What you see | Meaning |
|--------------|---------|
| The tools list shows ~30 `autopm` tools | The client reached the server ✅ |
| *"You are not signed in to Agile PM…"* | The server is reached, but no token arrived. Check the header name (`Authorization`), the `Bearer ` prefix, or `AGILEPM_TOKEN`. |
| *"Agile PM rejected the access token (401)…"* | The token arrived but is expired or wrong. See below. |
| No tools / "failed to connect" | HTTP: check the server is running and the URL is right. stdio: check the exe path and run it by hand with `--stdio` to see any startup error. |

### When the token expires

Tokens expire, and the server never refreshes them silently. When tools start returning *401*:

1. Call `refresh_token` (with the old access token and the refresh token) or `login`.
2. Put the new access token where the client reads it:
   - **Env-var based** (Claude Code `${AGILEPM_TOKEN}`, Cursor `${env:...}`, Codex `bearer_token_env_var`, ...): update the environment variable, then restart the client.
   - **Prompt based** (VS Code / Visual Studio `${input:...}`): clear the remembered value (VS Code: Command Palette → search "MCP" for the reset-inputs command) or restart the IDE, then enter the new token when prompted.
   - **Token written in the config**: edit the file, then restart the server from the client (or the client itself).
3. stdio servers read `AGILEPM_TOKEN` when they start, so the client must **restart the server** to pick up a new token.

### Keeping tokens safe in client configs

- Prefer the client's **secret prompt or env-var expansion** (see the table above) over pasting the token into a file.
- **Never commit** a config file that contains a real token. Shared files (`.mcp.json`, `.vscode/mcp.json`, `.cursor/mcp.json`) should only contain `${...}` references.
- Each person should use **their own** token. The server acts as whoever's token it receives.
- Tool approval prompts are a safety net. Auto-approve read-only tools if you like, but keep approval on for tools that create, update, move or reassign work.

---

## Tool reference

All IDs are GUIDs. Dates are ISO 8601 (`2026-10-31` or `2026-10-31T17:00:00Z`).
List tools return `{ "items": [...], "paging": { "page", "pageSize", "totalCount", "totalPages", "hasNextPage" } }`.
Write tools return `{ "succeeded": true, "summary": "...", "messages": [...], "data": ... }`.
Failures come back as MCP tool errors with a readable message (see [Troubleshooting](#troubleshooting)).

Annotations: **R** = read-only, **W** = changes data (non-destructive), **I** = idempotent (repeating it has the same effect).

### Auth: `Tools/Auth/AuthTools.cs`

| Tool | | Parameters | Agile PM endpoint | Notes |
|------|---|------------|-------------------|-------|
| `login` | W | `email`, `password` | `POST Auth/Login` | No token needed. Returns the access token, refresh token and expiry. |
| `refresh_token` | W | `jwtToken`, `refreshToken` | `POST Auth/RefreshToken` | No token needed. Returns a new token pair. |

### ProjectSetup: `Tools/ProjectSetup/ProjectTools.cs`

| Tool | | Parameters | Agile PM endpoint | Notes |
|------|---|------------|-------------------|-------|
| `list_my_projects` | R | `nameContains?`, `page=1`, `pageSize=20` | `GET ProjectSetup/getProjects` (all pages) | Only projects where the user is **project manager, scrum master or team member**, plus `myRole`. Start here. |
| `get_project` | R | `projectId` | `GET ProjectSetup/getProjects?id=` | Full project details, including team members. |
| `list_project_team_members` | R | `projectId` | `GET ProjectSetup/getProjects?id=` | Member user IDs, names, emails, roles and pending task counts. Use it to find assignee IDs. |

### TaskManagement: epics (`Tools/TaskManagement/EpicTools.cs`)

| Tool | | Parameters | Agile PM endpoint |
|------|---|------------|-------------------|
| `list_epics` | R | `projectId`, `page=1`, `pageSize=20` | `GET TaskManagement/getEpics` |
| `get_epic` | R | `epicId` | `GET TaskManagement/getEpics?id=` |
| `create_epic` | W | `projectId`, `title`, `description?`, `estimatedEffort=0`, `status=0`, `priority=0`, `startDate?`, `endDate?`, `phaseId?`, `milestoneId?` | `POST TaskManagement/AddEpic` |
| `update_epic` | W, I | `epicId`, `projectId`, `title?`, `description?`, `estimatedEffort?`, `status?`, `priority?`, `startDate?`, `endDate?` | `POST TaskManagement/UpdateEpic` |

> Epic `status` and `priority` are numeric codes (0–3) until the `EpicStatus` / `EpicPriority` names are added. `update_epic` needs `projectId` because Agile PM's epic response doesn't include it.

### TaskManagement: user stories (`Tools/TaskManagement/UserStoryTools.cs`)

| Tool | | Parameters | Agile PM endpoint | Notes |
|------|---|------------|-------------------|-------|
| `list_user_stories` | R | `projectId?`, `epicId?`, `assignedToMe=false`, `assignedToUserId?`, `page=1`, `pageSize=20` | `GET TaskManagement/getUserStorys` | Pass at least one filter. `assignedToMe` uses the user ID from your token. |
| `get_user_story` | R | `userStoryId` | `GET TaskManagement/getUserStorys?id=` | Includes assignments and tasks. |
| `create_user_story` | W | `epicId`, `story`, `description?`, `acceptanceCriteria?`, `priority=Medium`, `status=Planned`, `assignedTo?[]`, `workItemCategoryId?` | `POST TaskManagement/AddUserStory` | A story always belongs to an epic. |
| `update_user_story` | W, I | `userStoryId`, `story?`, `description?`, `acceptanceCriteria?`, `priority?`, `epicId?`, `assignedTo?[]` | `POST TaskManagement/UpdateUserStory` | `assignedTo` replaces the whole assignee list. |
| `move_user_story_status` | W, I | `userStoryId`, `status` | `POST TaskManagement/MoveUserStoryStatus` | **Deprecated.** A story's stage follows its tasks; move the tasks instead. |
| `reassign_user_story` | W | `userStoryId`, `fromUserId`, `toUserId` | `POST TaskManagement/ReAssignUserStory` | |

### TaskManagement: tasks (`Tools/TaskManagement/ProjectTaskTools.cs`)

| Tool | | Parameters | Agile PM endpoint | Notes |
|------|---|------------|-------------------|-------|
| `list_project_tasks` | R | `projectId?`, `userStoryId?`, `page=1`, `pageSize=20` | `GET TaskManagement/getProjectTasks` | Pass at least one filter. |
| `get_project_task` | R | `taskId` | `GET TaskManagement/getProjectTasks?id=` | Includes sub-tasks and comments. |
| `create_project_task` | W | `projectId`, `title`, `description?`, `userStoryId?`, `epicId?`, `assignTo?`, `plannedHours=0`, `endDate?`, `priority=Medium`, `complexity=Medium`, `status=Planned`, `skillId?`, `workBucketId?` | `POST TaskManagement/AddProjectTask` | |
| `update_project_task` | W, I | `taskId`, `title?`, `description?`, `plannedHours?`, `endDate?`, `priority?`, `complexity?`, `userStoryId?`, `epicId?` | `POST TaskManagement/UpdateProjectTask` | Stage and assignee are kept; use the two tools below to change them. |
| `move_task_status` | W | `taskId`, `status`, `manHours=0`, `subTaskId?` | `POST TaskManagement/MoveTaskStatus` | Logs hours worked. A task can't jump from `Planned` to `Completed`; it must go through `InProgress`. Pass `subTaskId` to move a sub-task. |
| `reassign_project_task` | W | `taskId`, `assignToUserId` | `POST TaskManagement/ReAssignProjectTask` | |
| `pause_task_work` | W | `taskId` | `POST TaskManagement/PauseTaskWork` | Pauses the work timer. |
| `resume_task_work` | W | `taskId` | `POST TaskManagement/ResumeTaskWork` | Resumes the work timer. |
| `get_dashboard_summary` | R | – | `GET TaskManagement/getDashboardSummary` | The user's dashboard counts. |

### TaskManagement: sub-tasks (`Tools/TaskManagement/SubTaskTools.cs`)

| Tool | | Parameters | Agile PM endpoint |
|------|---|------------|-------------------|
| `list_sub_tasks` | R | `taskId`, `page=1`, `pageSize=20` | `GET TaskManagement/getSubTasks` |
| `create_sub_task` | W | `taskId`, `title`, `description?`, `assignTo?`, `plannedHours=0`, `endDate?`, `priority=Medium`, `complexity=Medium`, `status=Planned`, `isBlocker=false`, `skillId?` | `POST TaskManagement/AddSubTask` |

### TaskManagement: comments (`Tools/TaskManagement/CommentTools.cs`)

| Tool | | Parameters | Agile PM endpoint | Notes |
|------|---|------------|-------------------|-------|
| `list_comments` | R | exactly one of `taskId?` / `subTaskId?` / `userStoryId?`, `page=1`, `pageSize=20` | `GET TaskManagement/getComments` | |
| `add_comment` | W | `remark`, exactly one of `taskId?` / `subTaskId?` / `userStoryId?`, `taggedUserIds?[]` | `POST TaskManagement/AddComment` | Tagged users are notified. |

### WorkManagement: `Tools/WorkManagement/MyWorkTools.cs`

| Tool | | Parameters | Agile PM endpoint | Notes |
|------|---|------------|-------------------|-------|
| `get_my_work` | R | `includeCompleted=false` | `GET WorkManagement/GetMyWork` | The user's tasks across all projects. The best answer to "what am I working on?". |

### Sample

| Tool | Notes |
|------|-------|
| `get_random_number` | The template's example tool (`Tools/RandomNumberTools.cs`). Delete it before release. |

---

## Enum reference

Agile PM sends these as integers. The tools accept and show the **names**, and the server converts them to numbers.

| Enum | Used for | Values (number = name) |
|------|----------|------------------------|
| `GeneralStagesEnum` | Task and sub-task stage | 0 `Planned`, 1 `InProgress`, 2 `Returned`, 3 `Completed`, 4 `Closed`, 5 `Cancelled` |
| `UserStoryStagesEnum` | User story stage | 0 `Planned`, 1 `In_Progress`, 2 `Completed`, 3 `Closed` |
| `PriorityEnum` | Priority and complexity | 0 `Low`, 1 `Medium`, 2 `High` |
| `EpicStatus`, `EpicPriority` | Epic status and priority | 0–3. Names not mapped yet. |

> These live in `Tools/Shared/AgilePmEnums.cs`. **The order of the members must match Agile PM's definitions exactly**, because the number sent is the member's position.

---

## Behaviour you should know about

**"My projects" is filtered by this server.** Agile PM's `getProjects` returns every project the caller can see. `list_my_projects` reads all pages, then keeps the projects where the user is the project manager, the scrum master or a team member. It identifies the user from the user-ID and email claims in their token (`nameid`, `sub`, `uid`, `email`, …). The token is only *read* here; Agile PM validates it on every forwarded request.

**Updates change only what you pass.** Agile PM's `Update*` endpoints replace the whole record. Each `update_*` tool reads the current record, applies only the arguments you gave, and sends the full record back. Updating just a title won't wipe the description, dates or assignees.

**No sub-task update tool, on purpose.** The sub-task response doesn't include some fields that `UpdateSubTask` requires (skill, complexity, blocker flag), so a read-modify-write would silently clear them. To change a sub-task's stage, use `move_task_status` with `subTaskId`.

**No delete tools.** Deleting is out of scope for v1 so an AI assistant can't remove data by mistake.

**Paging.** List tools return 20 items by default and at most 50 per page, so large projects don't flood the AI's context. Use `page` to read further.

**Errors are written for the AI to act on.** For example:

| Situation | Message |
|-----------|---------|
| No token supplied | *You are not signed in to Agile PM. Send the token in the Authorization header…* |
| 401 from Agile PM | *Agile PM rejected the access token (401)… use refresh_token or login* |
| 403 from Agile PM | *You do not have permission to do this in Agile PM (403).* |
| Agile PM returns `succeeded: false` | *Agile PM rejected the request: \<Agile PM's messages\>* |
| Validation (400) | *Agile PM returned 400 (BadRequest): \<field errors\>* |
| Network / timeout | *Could not reach Agile PM…* / *Agile PM did not respond in time…* |

---

## Building a standalone executable

The project publishes a **self-contained, single-file** executable. The .NET runtime and every dependency are bundled into one file, so it runs on any machine with the matching OS and CPU, with **no .NET installation**.

These settings in `AutoPM_MCP.csproj` make that happen:

```xml
<SelfContained>true</SelfContained>          <!-- bundle the .NET runtime -->
<PublishSelfContained>true</PublishSelfContained>
<PublishSingleFile>true</PublishSingleFile>  <!-- one executable instead of hundreds of DLLs -->
<RuntimeIdentifiers>win-x64;win-arm64;osx-arm64;linux-x64;linux-arm64;linux-musl-x64</RuntimeIdentifiers>
```

### How platform targeting works

A self-contained executable contains native code, so **each build targets exactly one OS + CPU combination**, called a *runtime identifier* (RID). A `win-x64` build won't run on Linux, and a `linux-x64` build won't run on an ARM Raspberry Pi. Build once for every platform you need to support.

**You can build every platform from any machine.** Windows can produce Linux and macOS executables, and Linux can produce Windows executables. The SDK downloads the right runtime pack the first time you target a new RID. The only exception is macOS code signing (see [macOS](#macos)).

### 1. Install the build tools (build machine only)

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Check it with `dotnet --version` (it should print `10.x`).
- Internet access to `nuget.org` for the first build of each RID.

The machines that *run* the executable need none of this.

### 2. Pick your target RIDs

| Target machine | RID | Configured | Notes |
|----------------|-----|:----------:|-------|
| Windows 10/11, Windows Server, 64-bit Intel/AMD | `win-x64` | ✅ | Most Windows PCs and servers |
| Windows on ARM (Surface Pro X, Copilot+ PCs) | `win-arm64` | ✅ | |
| Windows 32-bit | `win-x86` | ➕ | Rare; add it to the list first |
| macOS on Apple silicon (M1–M4) | `osx-arm64` | ✅ | |
| macOS on Intel | `osx-x64` | ➕ | Add it to the list first |
| Linux 64-bit Intel/AMD (Ubuntu, Debian, RHEL, Fedora, SUSE...) | `linux-x64` | ✅ | Most Linux servers and VMs |
| Linux ARM 64-bit (AWS Graviton, Ampere, Raspberry Pi 4/5 64-bit OS) | `linux-arm64` | ✅ | |
| Alpine Linux / musl, 64-bit | `linux-musl-x64` | ✅ | Small Docker images |
| Alpine Linux / musl, ARM 64-bit | `linux-musl-arm64` | ➕ | Add it to the list first |

✅ = already in `<RuntimeIdentifiers>`. ➕ = add it there first, for example:

```xml
<RuntimeIdentifiers>win-x64;win-arm64;win-x86;osx-arm64;osx-x64;linux-x64;linux-arm64;linux-musl-x64;linux-musl-arm64</RuntimeIdentifiers>
```

Full list: [.NET RID catalog](https://learn.microsoft.com/dotnet/core/rid-catalog).

**Not sure which RID a machine needs?**

| OS | Command | Output → RID |
|----|---------|--------------|
| Windows | `echo %PROCESSOR_ARCHITECTURE%` | `AMD64` → `win-x64`, `ARM64` → `win-arm64` |
| Linux | `uname -m` and `cat /etc/os-release` | `x86_64` → `linux-x64`, `aarch64` → `linux-arm64`. If `ID=alpine`, use `linux-musl-*` |
| macOS | `uname -m` | `arm64` → `osx-arm64`, `x86_64` → `osx-x64` |

### 3. Publish for one platform

Run from the project folder (the one containing `AutoPM_MCP.csproj`):

```bash
dotnet publish -c Release -r <RID> -o publish/<RID> -p:EnableCompressionInSingleFile=true
```

Examples:

```bash
dotnet publish -c Release -r win-x64        -o publish/win-x64        -p:EnableCompressionInSingleFile=true
dotnet publish -c Release -r win-arm64      -o publish/win-arm64      -p:EnableCompressionInSingleFile=true
dotnet publish -c Release -r linux-x64      -o publish/linux-x64      -p:EnableCompressionInSingleFile=true
dotnet publish -c Release -r linux-arm64    -o publish/linux-arm64    -p:EnableCompressionInSingleFile=true
dotnet publish -c Release -r linux-musl-x64 -o publish/linux-musl-x64 -p:EnableCompressionInSingleFile=true
dotnet publish -c Release -r osx-arm64      -o publish/osx-arm64      -p:EnableCompressionInSingleFile=true
```

What the options do:

| Option | Meaning |
|--------|---------|
| `-c Release` | Optimised build (always use it for anything you distribute) |
| `-r <RID>` | Target platform |
| `-o <folder>` | Where the output goes (default: `bin/Release/net10.0/<RID>/publish/`) |
| `-p:EnableCompressionInSingleFile=true` | Compresses the bundled runtime: about 106 MB → 51 MB on `win-x64`. First start is slightly slower. Optional but recommended. |
| `-p:Version=1.2.0` | Stamps a version into the executable (file properties on Windows). Optional. |
| `-p:DebugType=none` | Leaves out the `.pdb` debug symbols. Optional. |

The output is `AutoPM_MCP.exe` on Windows and `AutoPM_MCP` (no extension) on Linux and macOS.

### 4. Publish for all platforms at once

**PowerShell (Windows, or `pwsh` on Linux/macOS)**: builds each RID and zips it into `dist/`:

```powershell
$version = '1.0.0'
$rids = 'win-x64','win-arm64','osx-arm64','linux-x64','linux-arm64','linux-musl-x64'

New-Item -ItemType Directory -Force dist | Out-Null
foreach ($rid in $rids) {
    $out = "publish/$rid"
    Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
    dotnet publish -c Release -r $rid -o $out `
        -p:EnableCompressionInSingleFile=true -p:Version=$version -p:DebugType=none
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid" }

    # Keep only what is needed to run (see "What to ship")
    Get-ChildItem $out -Exclude 'AutoPM_MCP*','appsettings.json' | Remove-Item -Recurse -Force
    Compress-Archive -Path "$out/*" -DestinationPath "dist/AutoPM_MCP-$version-$rid.zip" -Force
}
```

**Bash (Linux/macOS, or Git Bash on Windows):**

```bash
#!/usr/bin/env bash
set -euo pipefail
version=1.0.0
rids=(win-x64 win-arm64 osx-arm64 linux-x64 linux-arm64 linux-musl-x64)

mkdir -p dist
for rid in "${rids[@]}"; do
  out="publish/$rid"
  rm -rf "$out"
  dotnet publish -c Release -r "$rid" -o "$out" \
    -p:EnableCompressionInSingleFile=true -p:Version="$version" -p:DebugType=none
  # Keep only what is needed to run
  find "$out" -mindepth 1 -maxdepth 1 ! -name 'AutoPM_MCP*' ! -name 'appsettings.json' -exec rm -rf {} +
  # tar keeps the executable bit for Linux/macOS
  tar -czf "dist/AutoPM_MCP-$version-$rid.tar.gz" -C "$out" .
done
```

> Zip files made on Windows don't keep the Unix "executable" permission. Linux/macOS users have to run `chmod +x AutoPM_MCP` after extracting. The Bash script's `.tar.gz` files keep the permission.

### 5. Build in CI (GitHub Actions example)

```yaml
# .github/workflows/release.yml
name: release
on:
  push:
    tags: [ 'v*' ]

jobs:
  publish:
    strategy:
      matrix:
        rid: [ win-x64, win-arm64, osx-arm64, linux-x64, linux-arm64, linux-musl-x64 ]
    runs-on: ubuntu-latest           # cross-builds every RID from Linux
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Publish
        run: >
          dotnet publish AutoPM_MCP/AutoPM_MCP.csproj -c Release -r ${{ matrix.rid }} -o out
          -p:EnableCompressionInSingleFile=true -p:DebugType=none
          -p:Version=${GITHUB_REF_NAME#v}
      - uses: actions/upload-artifact@v4
        with:
          name: AutoPM_MCP-${{ matrix.rid }}
          path: |
            out/AutoPM_MCP*
            out/appsettings.json
```

Azure DevOps Pipelines works the same way: one `DotNetCoreCLI@2` publish task per RID, with the same arguments.

### 6. What to ship

After publishing, the output folder contains:

| File | Ship it? | Why |
|------|:--------:|-----|
| `AutoPM_MCP.exe` / `AutoPM_MCP` | **Yes** | The whole application, runtime included. |
| `appsettings.json` | **Yes**, unless you configure everything through environment variables | API base URL, logging, transport. Must sit **next to the executable**. |
| `AutoPM_MCP.pdb` | Optional | Debug symbols, for line numbers in stack traces. |
| `web.config`, `aspnetcorev2_inprocess.dll` | Only for [IIS](#option-c-windows-server-with-iis) | Used by IIS only. |
| `AutoPM_MCP.staticwebassets.endpoints.json` | No | Not used by this server. |

**Running with only the executable.** Without `appsettings.json`, the API URL must come from the environment:

```bash
AgilePM__ApiBaseUrl=https://your-agilepm-host/api/ ./AutoPM_MCP --stdio
```

Without a base URL from either source, the server exits with code 1 and prints *"AgilePM:ApiBaseUrl … is required"*.

### 7. First run on each OS

#### Windows

- Run `AutoPM_MCP.exe` from a terminal, or point an MCP client at it.
- SmartScreen or antivirus may warn about an unsigned download. For distribution, sign it with your organisation's code-signing certificate:
  `signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a AutoPM_MCP.exe`
- If the file was downloaded, you may need to unblock it: right-click → Properties → *Unblock*, or `Unblock-File .\AutoPM_MCP.exe`.

#### Linux

```bash
chmod +x AutoPM_MCP
./AutoPM_MCP --stdio        # or ./AutoPM_MCP for HTTP mode
```

- .NET needs the ICU globalization libraries. Most distributions have them. On minimal images, install `libicu` (`apt install libicu-dev`, `dnf install libicu`, `apk add icu-libs`) or set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.
- Use `linux-musl-*` builds on Alpine and `linux-*` builds everywhere else.

#### macOS

```bash
chmod +x AutoPM_MCP
xattr -d com.apple.quarantine AutoPM_MCP   # only if it was downloaded through a browser
./AutoPM_MCP --stdio
```

- Apple silicon only runs signed code. Builds made **on a Mac** are ad-hoc signed automatically. Builds made **on Windows or Linux** are not, so sign them once on the Mac: `codesign --force --sign - AutoPM_MCP`. If you skip this, macOS kills the process with no message.
- For distribution to other people, sign with a Developer ID certificate and notarize the file.

### 8. Check the build

Run these on the target machine:

```bash
# stdio: should print one JSON line (the initialize response) and nothing else on stdout
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}' | ./AutoPM_MCP --stdio

# HTTP: start it, then list the tools from another terminal
./AutoPM_MCP --urls http://localhost:6015
curl -s http://localhost:6015/ -H "Accept: application/json, text/event-stream" -H "Content-Type: application/json" \
     -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### Why not trimming or Native AOT?

`PublishTrimmed` and `PublishAot` would make a much smaller file, but they would break this server. The MCP SDK finds tools by reflection (`WithToolsFromAssembly`), and the code uses reflection-based JSON. Trimming can strip tools or properties that look unused, and the failure is silent. Leave them off unless you also add source-generated JSON contexts and test every tool.

---

## Publishing and hosting on a server

In **HTTP mode**, one server instance serves many users. Each user's MCP client sends their own Agile PM token in the `Authorization` header, so the server holds no per-user state. This is the mode to host centrally. (stdio mode is for running on the user's own machine and is never hosted.)

### Before you host: checklist

| ✔ | Item | Why |
|---|------|-----|
| ☐ | **HTTPS** in front of the server | Bearer tokens travel in a header. Never expose plain HTTP beyond localhost. |
| ☐ | `McpTransport` is `Http` (the default) | stdio mode doesn't listen on a port. |
| ☐ | Listening address set (`ASPNETCORE_URLS` or `--urls`) | The default is `http://localhost:5000`, which can't be reached from other machines. |
| ☐ | `AgilePM:ApiBaseUrl` set, and the server can reach it | Check with `curl https://your-agilepm-host/coreswagger/index.html` from the server. |
| ☐ | Log levels raised to `Information` or `Warning` | `Debug` is noisy in production. |
| ☐ | Log file path is absolute, in a writable folder | It's relative to the working directory by default. |
| ☐ | CORS reviewed | `Program.cs` allows any origin. Narrow it if browser clients connect from known origins only. |
| ☐ | The reverse proxy **doesn't buffer responses** | MCP replies are sent as `text/event-stream`. Buffering delays or breaks them. |
| ☐ | `ASPNETCORE_ENVIRONMENT=Production` | This is the default for published builds; just don't set it to `Development`. |

### Server endpoint

| | |
|---|---|
| MCP endpoint | `POST /` (Streamable HTTP, stateless). Clients use the server's base URL, e.g. `https://autopm-mcp.example.com/` |
| Required request headers | `Content-Type: application/json`, `Accept: application/json, text/event-stream`, `Authorization: Bearer <Agile PM token>` |
| Health check | None built in. `POST /` with a `tools/list` body works as a liveness probe (it doesn't call Agile PM or need a token). |

### Server settings (environment variables)

```bash
ASPNETCORE_URLS=http://0.0.0.0:8080             # address:port to listen on (behind a proxy)
ASPNETCORE_ENVIRONMENT=Production
AgilePM__ApiBaseUrl=https://your-agilepm-host/api/
AgilePM__TimeoutSeconds=30
Serilog__MinimumLevel__Default=Information
Serilog__WriteTo__1__Args__path=/var/log/autopm-mcp/app-.log   # absolute log path (index 1 = the File sink)
```

You can also put these in `appsettings.json` next to the executable. Environment variables win.

---

### Option A: Linux VM with systemd + nginx (recommended)

This works on Ubuntu, Debian, RHEL and similar distributions. Use the `linux-x64` build (or `linux-arm64` on ARM servers).

**1. Copy the build to the server**

```bash
# on your machine
dotnet publish -c Release -r linux-x64 -o publish/linux-x64 -p:EnableCompressionInSingleFile=true
scp publish/linux-x64/AutoPM_MCP publish/linux-x64/appsettings.json user@server:/tmp/
```

**2. Install it**

```bash
# on the server
sudo useradd --system --no-create-home --shell /usr/sbin/nologin autopm
sudo mkdir -p /opt/autopm-mcp /var/log/autopm-mcp
sudo mv /tmp/AutoPM_MCP /tmp/appsettings.json /opt/autopm-mcp/
sudo chmod +x /opt/autopm-mcp/AutoPM_MCP
sudo chown -R autopm:autopm /opt/autopm-mcp /var/log/autopm-mcp
```

**3. Create the service:** `/etc/systemd/system/autopm-mcp.service`

```ini
[Unit]
Description=AutoPM MCP Server
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=autopm
Group=autopm
WorkingDirectory=/opt/autopm-mcp
ExecStart=/opt/autopm-mcp/AutoPM_MCP
Restart=always
RestartSec=5
# Listen on localhost only; nginx is the public entry point
Environment=ASPNETCORE_URLS=http://127.0.0.1:8080
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=Serilog__MinimumLevel__Default=Information
Environment=Serilog__WriteTo__1__Args__path=/var/log/autopm-mcp/app-.log
# Hardening
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=/var/log/autopm-mcp
PrivateTmp=true

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now autopm-mcp
sudo systemctl status autopm-mcp          # should be "active (running)"
journalctl -u autopm-mcp -f               # live console logs
```

**4. Put nginx in front with HTTPS:** `/etc/nginx/sites-available/autopm-mcp`

```nginx
server {
    listen 80;
    server_name autopm-mcp.example.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name autopm-mcp.example.com;

    ssl_certificate     /etc/letsencrypt/live/autopm-mcp.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/autopm-mcp.example.com/privkey.pem;

    location / {
        proxy_pass         http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_set_header   Authorization     $http_authorization;   # forward the bearer token

        # MCP responses are event streams: don't buffer them, and allow slow tool calls
        proxy_buffering    off;
        proxy_cache        off;
        proxy_read_timeout 300s;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/autopm-mcp /etc/nginx/sites-enabled/
sudo certbot --nginx -d autopm-mcp.example.com      # free Let's Encrypt certificate
sudo nginx -t && sudo systemctl reload nginx
```

**5. Update to a new version**

```bash
sudo systemctl stop autopm-mcp
sudo cp /tmp/AutoPM_MCP /opt/autopm-mcp/AutoPM_MCP && sudo chmod +x /opt/autopm-mcp/AutoPM_MCP
sudo systemctl start autopm-mcp
```

The server is stateless, so a restart only interrupts calls that are running at that moment.

---

### Option B: Docker

Use the `linux-x64` (or `linux-musl-x64` for Alpine) single-file build on top of Microsoft's `runtime-deps` image. That image contains only the OS libraries .NET needs, because the runtime is already inside the executable.

**`Dockerfile`** (multi-stage: builds inside Docker, so the build machine only needs Docker):

```dockerfile
# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AutoPM_MCP.csproj ./
RUN dotnet restore -r linux-x64
COPY . .
RUN dotnet publish -c Release -r linux-x64 -o /app --no-restore \
    -p:EnableCompressionInSingleFile=true -p:DebugType=none

# ---- run ----
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /app
COPY --from=build /app/AutoPM_MCP /app/appsettings.json ./
RUN mkdir -p /app/logs && chown app /app/logs     # the non-root user must be able to write logs
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Serilog__MinimumLevel__Default=Information
EXPOSE 8080
USER app
ENTRYPOINT ["./AutoPM_MCP"]
```

Add a `.dockerignore` with `bin/`, `obj/`, `publish/`, `logs/` to keep the build context small.

```bash
docker build -t autopm-mcp:1.0.0 .
docker run -d --name autopm-mcp -p 8080:8080 --restart unless-stopped \
  -e AgilePM__ApiBaseUrl=https://your-agilepm-host/api/ \
  autopm-mcp:1.0.0
docker logs -f autopm-mcp
```

- In containers, prefer console logs (`docker logs`, your log collector) over the file sink. If you keep the file sink, mount a volume for it: `-v autopm-logs:/app/logs`.
- Terminate TLS at your ingress or load balancer (Kubernetes Ingress, Traefik, nginx, Azure Container Apps, AWS ALB). Disable response buffering there for `text/event-stream`, as in the nginx example.
- The container is stateless: scale it out with multiple replicas and no sticky sessions.

---

### Option C: Windows Server with IIS

IIS hosts the app through the **ASP.NET Core Module**, which manages the process and forwards requests to it.

**1. Prepare the server (once)**

- Install IIS (Server Manager → Add Roles → *Web Server (IIS)*).
- Install the **ASP.NET Core Hosting Bundle** for .NET 10 from <https://dotnet.microsoft.com/download/dotnet/10.0>. That's needed even for self-contained apps, because it installs the ASP.NET Core Module into IIS. Afterwards, run `iisreset`.

**2. Publish and copy**

```powershell
dotnet publish -c Release -r win-x64 -o publish\win-x64 -p:EnableCompressionInSingleFile=true
# copy the whole folder (including web.config) to the server, e.g. C:\inetpub\autopm-mcp
```

**3. Switch `web.config` to out-of-process hosting.** The generated `web.config` uses `hostingModel="inprocess"`, which **doesn't support single-file executables**. Edit it on the server (or keep a copy in the project) so it reads:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath=".\AutoPM_MCP.exe" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout"
                  hostingModel="outofprocess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```

With out-of-process hosting, IIS chooses the port and passes it to the app. **Don't** set `ASPNETCORE_URLS`.

**4. Create the site**

1. IIS Manager → *Application Pools* → **Add**: name `AutoPM_MCP`, .NET CLR version **No Managed Code**.
2. *Sites* → **Add Website**: physical path `C:\inetpub\autopm-mcp`, app pool `AutoPM_MCP`, an **https** binding with your certificate.
3. Give the app pool identity write access to the logs folder:
   `icacls C:\inetpub\autopm-mcp\logs /grant "IIS AppPool\AutoPM_MCP:(OI)(CI)M"` (create the folder first).
4. If calls take a long time, raise `requestTimeout` on the `<aspNetCore>` element (default 2 minutes).

**Troubleshooting IIS:** a *500.30* or *502.5* error means the process failed to start. Temporarily set `stdoutLogEnabled="true"` and read `logs\stdout_*.log`.

**Updating:** drop an `app_offline.htm` file into the site folder (IIS stops the app and releases the exe), replace `AutoPM_MCP.exe`, then delete `app_offline.htm`.

---

### Option D: Windows Server as a Windows Service (without IIS)

> ⚠️ **This needs a small code change first.** The service control manager expects the process to report its status. Without Windows Service integration, `sc start` fails with *error 1053*.

**1. Add Windows Service support (one-time code change):**

```bash
dotnet add package Microsoft.Extensions.Hosting.WindowsServices
```

In `Program.cs`, in `RunHttpAsync`, after `var builder = WebApplication.CreateBuilder(args);` add:

```csharp
builder.Host.UseWindowsService();   // no effect when not running as a service
```

**2. Publish, copy and register:**

```powershell
dotnet publish -c Release -r win-x64 -o publish\win-x64 -p:EnableCompressionInSingleFile=true
# copy publish\win-x64\AutoPM_MCP.exe and appsettings.json to C:\Services\AutoPM_MCP

New-Service -Name "AutoPM_MCP" -DisplayName "AutoPM MCP Server" `
  -BinaryPathName '"C:\Services\AutoPM_MCP\AutoPM_MCP.exe" --urls http://127.0.0.1:8080' `
  -StartupType Automatic
sc.exe failure AutoPM_MCP reset= 86400 actions= restart/5000/restart/5000/restart/5000
Start-Service AutoPM_MCP
```

- Services start in `C:\Windows\System32`. Set an absolute log path in `appsettings.json`, e.g. `C:\\Services\\AutoPM_MCP\\logs\\app-.log`.
- Put IIS (with URL Rewrite + ARR), nginx or another reverse proxy in front for HTTPS, or configure a Kestrel HTTPS endpoint with a certificate in `appsettings.json`.

---

### Option E: Azure App Service

1. Create a Web App: **Linux** (runtime stack: any, e.g. .NET 10) or **Windows**.
2. Publish the matching build (`linux-x64` or `win-x64`) and deploy the folder:
   ```bash
   dotnet publish -c Release -r linux-x64 -o publish/linux-x64 -p:EnableCompressionInSingleFile=true
   cd publish/linux-x64 && zip -r ../app.zip . && cd -
   az webapp deploy --resource-group <rg> --name <app> --src-path publish/app.zip --type zip
   ```
3. Linux only: set the startup command to `./AutoPM_MCP`.
4. Configuration → *Application settings*: add `AgilePM__ApiBaseUrl` and any other settings (App Service provides the port and HTTPS).
5. On Windows App Service, use the out-of-process `web.config` from [Option C](#option-c-windows-server-with-iis).

---

### After hosting: point clients at it

```bash
claude mcp add --transport http autopm https://autopm-mcp.example.com/ --header "Authorization: Bearer <token>"
```

For other clients, see [Connecting an MCP client](#connecting-an-mcp-client) and replace `http://localhost:6015` with your server's HTTPS URL.

### stdio mode distribution

stdio mode isn't hosted. Give users the executable for their platform (and `appsettings.json`) and the client config from [Connecting an MCP client](#connecting-an-mcp-client). Their MCP client starts it locally.

---

## Logging

Logging uses [Serilog](https://serilog.net/) and is configured in the `Serilog` section of `appsettings.json`.

- **Console:** human-readable. In stdio mode it's automatically sent to **stderr**, because stdout carries the MCP protocol.
- **File:** JSON lines in `logs/app-YYYYMMDD.log`, rolled daily, last 31 days kept. The path is **relative to the current working directory**. For stdio mode, set an absolute path (`Serilog:WriteTo:1:Args:path`), because MCP clients may start the server from any directory.
- **Levels:** `Debug` by default. `Microsoft.AspNetCore` and `System` are at `Warning`, and `ModelContextProtocol` is at `Information` so tool arguments (passwords!) are never logged. Use `Information` or `Warning` in production.
- **Never logged:** tokens, passwords and request bodies. Agile PM calls are logged as method + path + status code only.

---

## Project structure

```
AutoPM_MCP/
├── Program.cs                       # Startup: picks HTTP or stdio, wires logging, MCP and Agile PM services
├── appsettings.json                 # API base URL, transport, logging
├── AutoPM_MCP.http                  # Ready-made requests for manual testing
├── Helpers/                         # Everything about talking to Agile PM
│   ├── AgilePmClient.cs             # Typed HttpClient: GET/POST, get-by-id, read-all-pages, error mapping
│   ├── AgilePmResponse.cs           # Unwraps Result / PaginatedResult / Auth envelopes; extracts error messages
│   ├── AgilePmQuery.cs              # Query string builder (paging.pageNumber, ProjectId, ...); page-size limits
│   ├── AgilePmOptions.cs            # "AgilePM" settings + McpTransportMode enum
│   ├── AccessTokenProviders.cs      # Where the token comes from: HTTP header or AGILEPM_TOKEN
│   ├── BearerTokenHandler.cs        # Adds "Authorization: Bearer" to every outgoing request
│   ├── CurrentUser.cs               # Reads the caller's user ID and email from the token claims
│   ├── ToolResponse.cs              # Shapes tool output (items + paging, success summaries)
│   ├── JsonElementExtensions.cs     # Null-safe JSON property helpers
│   └── ServiceCollectionExtensions.cs # AddAgilePm(): registers all of the above
└── Tools/                           # MCP tools, one folder per Agile PM controller
    ├── Auth/AuthTools.cs
    ├── ProjectSetup/ProjectTools.cs
    ├── TaskManagement/
    │   ├── EpicTools.cs
    │   ├── UserStoryTools.cs
    │   ├── ProjectTaskTools.cs
    │   ├── SubTaskTools.cs
    │   ├── CommentTools.cs
    │   └── TaskManagementModels.cs  # Request/response records for this controller
    ├── WorkManagement/MyWorkTools.cs
    ├── Shared/AgilePmEnums.cs       # Enums shared across controllers
    └── RandomNumberTools.cs         # Template sample
```

---

## Adding a new tool

1. **Find the endpoint** in the [Swagger docs](https://your-agilepm-host/coreswagger/index.html) and note which controller it belongs to.
2. **Choose the file:** `Tools/<Controller>/<Area>Tools.cs`. Create the folder or class if needed.
3. **Write the method.** Inject `AgilePmClient` (and `CurrentUser` if you need the caller's identity) through the constructor:

   ```csharp
   [McpServerToolType]
   internal class SprintTools(AgilePmClient client)
   {
       [McpServerTool(ReadOnly = true)]
       [Description("Lists the sprints in a project.")]
       public async Task<string> ListSprints(
           [Description("Project ID")] Guid projectId,
           [Description("Page number, starting at 1")] int page = 1,
           [Description("Sprints per page (max 50)")] int pageSize = AgilePmQuery.DefaultPageSize,
           CancellationToken cancellationToken = default)
       {
           var query = AgilePmQuery.Paged(page, pageSize).Add("ProjectId", projectId);
           return ToolResponse.List(await client.GetAsync("TaskManagement/getSprints", query, cancellationToken));
       }
   }
   ```

4. **Rules of thumb:**
   - Paths are **relative and have no leading `/`** (`"TaskManagement/getSprints"`, not `"/api/TaskManagement/getSprints"`). The base URL already ends in `/api/`, and a leading slash would drop it.
   - Mark the tool: `ReadOnly = true` for reads, `Destructive = false` for non-destructive writes, `Idempotent = true` where repeating is harmless.
   - Write `[Description]`s for the AI: say what the tool is for, when to use it and what each parameter means. The description is the tool's documentation.
   - For **update** endpoints, read the current record and merge (see `UpdateProjectTask`). Never send a partial record.
   - Throw `McpException("...")` for input errors. Its message reaches the AI as-is.
   - Put request/response records in `<Controller>Models.cs` and shared enums in `Tools/Shared/`.
5. **Done.** `WithToolsFromAssembly()` registers the tool automatically. The tool name is the method name in `snake_case` (`ListSprints` → `list_sprints`).

---

## Testing

- **`AutoPM_MCP.http`:** open it in Visual Studio or VS Code (REST Client) to call `login`, `tools/list` and `list_my_projects` directly. Paste a token into `@AccessToken`.
- **MCP Inspector:** `npx @modelcontextprotocol/inspector`. Connect to `http://localhost:6015` with an `Authorization` header, or start the executable with `--stdio`.
- **curl** (HTTP mode):

  ```bash
  curl http://localhost:6015/ \
    -H "Accept: application/json, text/event-stream" \
    -H "Content-Type: application/json" \
    -H "MCP-Protocol-Version: 2025-11-25" \
    -H "Authorization: Bearer <token>" \
    -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_my_work"}}'
  ```

---

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| Exits at once with *"AgilePM:ApiBaseUrl … is required"* | `appsettings.json` isn't next to the executable, or the URL is empty. Ship the file or set `AgilePM__ApiBaseUrl`. |
| *"You are not signed in to Agile PM"* | No token reached the server. HTTP: check the client sends `Authorization: Bearer <token>`. stdio: check `AGILEPM_TOKEN` is set in the client's server config. |
| *"Agile PM rejected the access token (401)"* | Token expired or invalid. Call `refresh_token` (or `login`) and update the configured token. |
| *"…(403)"* | The user lacks permission in Agile PM for that action. |
| `list_my_projects` returns nothing | The token may not carry a recognised user-ID or email claim, or the user isn't a member of any project. Decode the token at <https://jwt.io> and compare its claims with the list in `Helpers/CurrentUser.cs`. |
| stdio client says the server sent invalid JSON | Something wrote to stdout. Logging is redirected to stderr automatically; check any custom code for `Console.WriteLine`. |
| VS Code can't connect to `https://localhost:5062` | Known issue with self-signed dev certificates ([microsoft/vscode#248170](https://github.com/microsoft/vscode/issues/248170)). Use `http://localhost:6015`. |
| `move_task_status` rejected | Tasks must go `Planned → InProgress → Completed`; they can't skip `InProgress`. |
| No `logs/` folder where expected | The file log path is relative to the working directory. Use an absolute path for stdio mode. |

---

## Security notes

- **Tokens are never stored** by the server. Each request uses the token that came with it.
- `login` takes a password as a tool argument, so the password passes through the AI client's conversation. Where possible, users should get their token outside the AI chat (for example with the `.http` file) and paste only the token into the client config.
- Tokens returned by `login` and `refresh_token` appear in the conversation. Treat chat transcripts accordingly.
- Use HTTPS for any HTTP deployment that isn't localhost.
- The server reads token claims only to filter results. It doesn't use them for security decisions; Agile PM checks authorisation on every call.
- No delete tools are exposed.

---

## Known limitations and roadmap

- `EpicStatus` and `EpicPriority` aren't mapped to names yet (numeric 0–3).
- `move_user_story_status` is deprecated. A story's stage follows its tasks.
- Not covered yet: sprints, milestones, phases, reports, file uploads and downloads, deletes.
- `get_random_number` (template sample) is still registered.

---

## More information

- [Model Context Protocol](https://modelcontextprotocol.io/) and [specification](https://spec.modelcontextprotocol.io/)
- [MCP C# SDK](https://csharp.sdk.modelcontextprotocol.io/)
- [.NET single-file deployment](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview)
- [.NET runtime identifiers](https://learn.microsoft.com/dotnet/core/rid-catalog)
