#!/usr/bin/env node

import { spawnSync } from "node:child_process";

const result = spawnSync("dotnet", ["list", "package", "--vulnerable", "--include-transitive", "--no-restore"], {
  encoding: "utf8",
  stdio: ["inherit", "pipe", "inherit"],
});
process.stdout.write(result.stdout ?? "");
if (result.error) throw result.error;
if (result.status !== 0) process.exit(result.status ?? 1);
if ((result.stdout ?? "").includes("has the following vulnerable packages")) process.exit(1);
