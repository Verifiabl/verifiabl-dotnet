#!/usr/bin/env node

import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = resolve(fileURLToPath(new URL("..", import.meta.url)));

export function issuerVersion(project) {
  const version = readFileSync(project, "utf8").match(/<Version>([^<]+)<\/Version>/)?.[1];
  if (!version) throw new Error(`Could not read Version from ${project}`);
  return version;
}

export function packageSourceConfig(packageDirectory) {
  return `<configuration>
  <packageSources>
    <clear />
    <add key="packed" value="${packageDirectory}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="packed"><package pattern="Verifiabl.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
`;
}

function runWithEnvironment(environment, ...arguments_) {
  const result = spawnSync("dotnet", arguments_, {
    cwd: root,
    env: environment,
    stdio: "inherit",
  });
  if (result.error) throw result.error;
  if (result.status !== 0) process.exit(result.status ?? 1);
}

function run(...arguments_) {
  runWithEnvironment(process.env, ...arguments_);
}

export function check(packageDirectory = "artifacts") {
  const packages = resolve(root, packageDirectory);
  const project = "examples/SelfManagedIssuer/SelfManagedIssuer.csproj";
  const version = issuerVersion(join(root, "src/Verifiabl/Verifiabl.csproj"));
  const temporary = mkdtempSync(join(tmpdir(), "verifiabl-dotnet-package-check-"));
  try {
    const config = join(temporary, "NuGet.config");
    writeFileSync(config, packageSourceConfig(packages));
    run("restore", project, "--configfile", config, "--packages", join(temporary, "packages"), `--property:VerifiablIssuerVersion=${version}`);
    run("build", project, "--configuration", "Release", "--no-restore", `--property:VerifiablIssuerVersion=${version}`);
    runWithEnvironment(
      { ...process.env, VERIFIABL_EXAMPLE_OUTPUT_DIR: join(temporary, "output") },
      "run",
      "--project",
      project,
      "--configuration",
      "Release",
      "--no-build",
      `--property:VerifiablIssuerVersion=${version}`,
      "--",
      "offline",
    );
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) check(process.argv[2]);
