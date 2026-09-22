# Quickstart: Security Posture Intelligence Navigator Chat Application

**Feature**: 006-chat-app | **Date**: 2026-02-23

## Prerequisites

| Tool | Version | Check Command |
|------|---------|---------------|
| .NET SDK | 9.0+ | `dotnet --version` |
| Node.js | 20.18.1+ | `node --version` |
| npm | 10+ | `npm --version` |

## 1. Clone & Branch

```bash
git checkout 006-chat-app
```

## 2. Create the Chat Project

```bash
# From repo root
dotnet new web -n Ato.Copilot.Chat -o src/Ato.Copilot.Chat --framework net9.0
dotnet sln Ato.Copilot.sln add src/Ato.Copilot.Chat/Ato.Copilot.Chat.csproj

# Add project references
dotnet add src/Ato.Copilot.Chat reference src/Ato.Copilot.Core/Ato.Copilot.Core.csproj
dotnet add src/Ato.Copilot.Chat reference src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj
dotnet add src/Ato.Copilot.Chat reference src/Ato.Copilot.State/Ato.Copilot.State.csproj

# Add NuGet packages
dotnet add src/Ato.Copilot.Chat package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/Ato.Copilot.Chat package Microsoft.EntityFrameworkCore.SqlServer
dotnet add src/Ato.Copilot.Chat package Microsoft.EntityFrameworkCore.Design
dotnet add src/Ato.Copilot.Chat package Serilog.AspNetCore
dotnet add src/Ato.Copilot.Chat package Serilog.Sinks.Console
```

## 3. Scaffold Frontend

```bash
cd src/Ato.Copilot.Chat

# Create React app with TypeScript
npx create-react-app ClientApp --template typescript

cd ClientApp

# Install dependencies
npm install @microsoft/signalr@^8.0.0 axios react-markdown react-syntax-highlighter
npm install -D @types/react-syntax-highlighter tailwindcss@^3.4.17 postcss autoprefixer

# Initialize Tailwind
npx tailwindcss init -p

cd ../../..
```

## 4. Verify Build

```bash
# Backend
dotnet build Ato.Copilot.sln

# Frontend
cd src/Ato.Copilot.Chat/ClientApp && npm run build && cd ../../..
```

## 5. Database Setup (SQLite — Development)

The `ChatDbContext` auto-creates the SQLite database on first run. Default connection string in `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "ChatDb": "Data Source=chat.db"
  }
}
```

To create the initial migration:

```bash
cd src/Ato.Copilot.Chat
dotnet ef migrations add InitialCreate --context ChatDbContext
dotnet ef database update --context ChatDbContext
cd ../..
```

## 6. Run

```bash
# Terminal 1 — Backend (serves API + SignalR hub + SPA in production mode)
cd src/Ato.Copilot.Chat
dotnet run --launch-profile https

# Terminal 2 — Frontend (development proxy to backend)
cd src/Ato.Copilot.Chat/ClientApp
npm start
```

- **Frontend dev server**: http://localhost:3000 (proxies API calls to backend)
- **Backend**: https://localhost:5001
- **SignalR Hub**: wss://localhost:5001/hubs/chat

## 7. Verify It Works

1. Open http://localhost:3000 in a browser
2. A new conversation should auto-create
3. Type a message and press Enter (or click Send)
4. Verify the message appears in the chat panel
5. Check browser DevTools → Network → WS tab for SignalR frames

## Project Structure After Setup

```
src/Ato.Copilot.Chat/
├── Ato.Copilot.Chat.csproj
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── ClientApp/                    # React 18 + TypeScript SPA
│   ├── package.json
│   ├── tsconfig.json
│   ├── tailwind.config.js
│   ├── public/
│   └── src/
│       ├── components/           # React components
│       ├── contexts/             # ChatContext (useReducer)
│       ├── hooks/                # useSignalR, useChat
│       ├── services/             # API client (Axios)
│       ├── types/                # TypeScript interfaces
│       ├── App.tsx
│       └── index.tsx
├── Controllers/                  # REST API controllers
├── Data/                         # ChatDbContext, migrations
├── Hubs/                         # SignalR ChatHub
├── Models/                       # DTOs, request/response types
└── Services/                     # ChatOrchestrator, business logic
```

## Key Configuration

### SPA Proxy (Development)

In `Ato.Copilot.Chat.csproj`:

```xml
<PropertyGroup>
  <SpaRoot>ClientApp\</SpaRoot>
  <SpaProxyServerUrl>http://localhost:3000</SpaProxyServerUrl>
  <SpaProxyLaunchCommand>npm start</SpaProxyLaunchCommand>
</PropertyGroup>
```

### EF Core Dual Provider

In `Program.cs`, the DbContext provider is selected by connection string prefix:

```csharp
builder.Services.AddDbContext<ChatDbContext>(options =>
{
    var conn = builder.Configuration.GetConnectionString("ChatDb")!;
    if (conn.StartsWith("Data Source="))
        options.UseSqlite(conn);
    else
        options.UseSqlServer(conn);
});
```

### SignalR Registration

```csharp
builder.Services.AddSignalR();
// ...
app.MapHub<ChatHub>("/hubs/chat");
```
