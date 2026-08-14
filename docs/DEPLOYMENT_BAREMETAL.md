# Bare Metal / VMs

_Part of the [ConduitSharp documentation](../README.md)._


Runs on AD-joined, ERP, and IIS boxes, where Kong and APISIX cannot. No .NET runtime install; the
binaries bundle it.

#### Linux (self-contained binary)

Download `conduitsharp-vX.X.X-linux-x64.tar.gz` from the [releases page](https://github.com/liqngliz/ConduitSharp/releases).

```bash
tar xzf conduitsharp-linux-x64.tar.gz -C /opt/conduitsharp
chmod +x /opt/conduitsharp/ConduitSharp.Host
# Edit /opt/conduitsharp/Configuration/routes.json
/opt/conduitsharp/ConduitSharp.Host
```

#### Windows Service / IIS

Download `conduitsharp-vX.X.X-win-x64.zip` from the
[releases page](https://github.com/liqngliz/ConduitSharp/releases).

**Run directly:**
```powershell
Expand-Archive conduitsharp-win-x64.zip C:\conduitsharp
# Edit C:\conduitsharp\Configuration\routes.json
C:\conduitsharp\ConduitSharp.Host.exe
```

**Run as a Windows Service:**

Service Control Manager restarts the process on failure.

```powershell
Expand-Archive conduitsharp-win-x64.zip C:\conduitsharp
# Edit C:\conduitsharp\Configuration\routes.json
sc.exe create ConduitSharp binPath="C:\conduitsharp\ConduitSharp.Host.exe" start=auto
sc.exe start ConduitSharp
```

To update: `sc.exe stop ConduitSharp` → replace the exe → `sc.exe start ConduitSharp`. No IIS or Hosting Bundle required.

**Host under IIS (in-process):**

Gateway runs inside the IIS worker process. IIS owns the lifecycle and the 80/443 binding.

1. Install the [ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/permalink/dotnetcore-current-windows-runtime-bundle-installer) on the server (one-time).
2. Extract the zip to e.g. `C:\inetpub\conduitsharp\` (exe, `web.config`, `Configuration\routes.json`).
3. In IIS Manager: **Add Website** → Physical path: `C:\inetpub\conduitsharp` → Application Pool → `.NET CLR Version: No Managed Code`.
4. Edit `Configuration\routes.json` and start the site.

`web.config` ships in the zip. IIS reads it and launches the exe via `AspNetCoreModuleV2`. Nothing else to configure.

**Host under IIS (reverse proxy):**

IIS listens on 80/443 and forwards to ConduitSharp on a local port. Use when IIS already serves other sites on the box and 443 must be shared via an SNI binding.

Run ConduitSharp as a Windows Service on a private port (e.g. 5000), then add an IIS site with an **Application Request Routing** (ARR) reverse proxy rule pointing at it:

```powershell
# 1. Start ConduitSharp on a local port
sc.exe create ConduitSharp binPath="C:\conduitsharp\ConduitSharp.Host.exe" start=auto
sc.exe start ConduitSharp
# (set ASPNETCORE_URLS=http://localhost:5000 in the service environment)

# 2. In IIS — requires ARR and URL Rewrite modules (install via Web Platform Installer)
#    Create a blank site on port 443 with your cert, then add a URL Rewrite inbound rule:
#      Pattern:      (.*)
#      Action type:  Rewrite
#      Rewrite URL:  http://localhost:5000/{R:1}
```

IIS terminates TLS and owns the certificates. ConduitSharp runs plain HTTP on loopback.

---

