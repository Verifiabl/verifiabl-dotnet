#!/usr/bin/env node

import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import assert from "node:assert/strict";
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
    const output = join(temporary, "output");
    runWithEnvironment(
      { ...process.env, VERIFIABL_EXAMPLE_OUTPUT_DIR: output },
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
    const [runDirectory] = readdirSync(output);
    assert.ok(runDirectory, "Offline example must produce a run directory");
    const manifest = (group, id) => JSON.parse(readFileSync(
      join(output, runDirectory, group, id, "manifest.json"), "utf8"));
    for (const [id, schema, currency, gross, taxField, tax] of [
      ["PAY-1001", "au.payslip.v2", "AUD", "9000.00", "paygw", "2250.00"],
      ["PAY-1002", "nz.payslip.v2", "NZD", "7600.00", "paye", "1710.00"],
    ]) {
      const batch = manifest("batch", id);
      assert.equal(batch.verifiablReference, batch.registrationRequest.verifiablReference);
      assert.equal(readFileSync(
        join(output, runDirectory, "batch", id, "xmp-payload.txt"), "utf8",
      ).startsWith(`2|${batch.verifiablReference}|`), true);
      assert.ok(!JSON.stringify(batch).includes(id === "PAY-1001" ? "Jane A. Doe" : "Zoë Nguyễn"));
      assert.equal(batch.registrationRequest.schema, schema);
      const fields = batch.registrationRequest.payslipNonPii;
      assert.deepEqual(fields, {
        period_end: "2026-08-31",
        payment_date: "2026-09-04",
        currency,
        gross,
        [taxField]: tax,
        net: id === "PAY-1001" ? "6750.00" : "5890.00",
      });
    }
    const single = manifest("single", "PAY-1001").registrationRequest;
    assert.equal(single.schema, "au.payslip.v2");
    assert.deepEqual(single.payslipNonPii, manifest("batch", "PAY-1001").registrationRequest.payslipNonPii);
    assert.notEqual(single.encryptionMetadata.iv, manifest("batch", "PAY-1001").registrationRequest.encryptionMetadata.iv);
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) check(process.argv[2]);
