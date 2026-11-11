import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { createElectronRuntimeEnv } from "./electronEnv.js";

const require = createRequire(import.meta.url);
const electronPath = require("electron");
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const appRoot = path.resolve(__dirname, "..");

// 산출물은 git 제외 영역인 Docs/Workflow/Local/artifacts 아래에 둔다.
const projectRoot = path.resolve(appRoot, "../../../..");
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const outDir = process.env.UI_AUDIT_DIR
  || path.join(projectRoot, "Docs", "Workflow", "Local", "artifacts", "ui-audit", stamp);

const forwarded = process.argv.slice(2).map((arg) =>
  arg === "--baseline-write" ? "--ui-audit-write-baseline" : arg);
const args = [appRoot, `--ui-audit-dir=${outDir}`, ...forwarded];
const child = spawn(electronPath, args, {
  cwd: appRoot,
  env: createElectronRuntimeEnv(),
  stdio: "inherit",
  windowsHide: false
});

child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }
  process.exit(code ?? 1);
});

child.on("error", (error) => {
  console.error(error);
  process.exit(1);
});
