# Bare Metal / VMs — the legacy estate

_Part of the [ConduitSharp documentation](../README.md)._


The reason ConduitSharp runs where Kong and APISIX cannot: on the AD-joined, ERP, and IIS boxes the
target workloads actually live on. No .NET runtime required — the runtime is bundled inside the binaries.

#### Linux — self-contained binary

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

For always-on deployments. The Service Control Manager restarts the process automatically on failure.

```powershell
Expand-Archive conduitsharp-win-x64.zip C:\conduitsharp
# Edit C:\conduitsharp\Configuration\routes.json
sc.exe create ConduitSharp binPath="C:\conduitsharp\ConduitSharp.Host.exe" start=auto
sc.exe start ConduitSharp
```

To update: `sc.exe stop ConduitSharp` → replace the exe → `sc.exe start ConduitSharp`. No IIS or Hosting Bundle required.

**Host under IIS (in-process):**

IIS manages the process lifecycle and handles port 80/443 binding. The gateway runs inside the IIS worker process.

1. Install the [ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/permalink/dotnetcore-current-windows-runtime-bundle-installer) on the server (one-time).
2. Extract the zip to e.g. `C:\inetpub\conduitsharp\` — it contains the exe, `web.config`, and `Configuration\routes.json`.
3. In IIS Manager: **Add Website** → Physical path: `C:\inetpub\conduitsharp` → Application Pool → `.NET CLR Version: No Managed Code`.
4. Edit `Configuration\routes.json` and start the site.

The `web.config` is generated automatically in the zip. IIS reads it and launches the exe via `AspNetCoreModuleV2`; you do not need to configure anything else.

**Host under IIS (reverse proxy):**

IIS listens on port 80/443 and forwards traffic to ConduitSharp running on a local port. Useful when IIS is already managing other sites on the same machine and you want to share port 443 with an SNI-based binding.

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

With this setup IIS handles TLS termination and certificate management, and ConduitSharp runs over plain HTTP on the loopback interface.

---

