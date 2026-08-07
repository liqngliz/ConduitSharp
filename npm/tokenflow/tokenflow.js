#!/usr/bin/env node
// Runtime is fetched here rather than in a postinstall hook: npm hides lifecycle-script
// output unless --foreground-scripts, which turns the one-time 46 MB download into a
// silent minute. Cached under the home directory so a version bump reuses it.
//
// Lives at the package root, not bin/, because .gitignore:2 ignores bin/.
const { execFileSync, spawn } = require('child_process');
const path = require('path'), fs = require('fs'), os = require('os');

const win = process.platform === 'win32';
const home = path.join(os.homedir(), '.tokenflow', 'dotnet');
const exe = path.join(home, win ? 'dotnet.exe' : 'dotnet');

if (!fs.existsSync(exe)) {
  console.error('tokenflow: first run, fetching the .NET 10 runtime (~46 MB) into ' + home);
  fs.mkdirSync(home, { recursive: true });
  const name = win ? 'dotnet-install.ps1' : 'dotnet-install.sh';
  const script = path.join(os.tmpdir(), name);
  execFileSync('curl', ['-#', '-L', `https://dot.net/v1/${name}`, '-o', script], { stdio: 'inherit' });
  const args = win
    ? ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', script,
       '-Runtime', 'aspnetcore', '-Channel', '10.0', '-InstallDir', home, '-NoPath']
    : [script, '--runtime', 'aspnetcore', '--channel', '10.0', '--install-dir', home, '--no-path'];
  if (!win) fs.chmodSync(script, 0o755);
  execFileSync(win ? 'powershell' : 'bash', args, { stdio: 'inherit' });
  console.error('tokenflow: runtime ready\n');
}

const child = spawn(exe, [path.join(__dirname, 'app', 'ConduitSharp.Spend.dll'), ...process.argv.slice(2)],
                    { stdio: 'inherit' });
child.on('exit', c => process.exit(c ?? 1));
