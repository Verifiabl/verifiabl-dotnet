#!/usr/bin/env node

import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import assert from "node:assert/strict";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = resolve(fileURLToPath(new URL("..", import.meta.url)));

export function packageVersion(project) {
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

function capture(environment, input, ...arguments_) {
  const result = spawnSync("dotnet", arguments_, {
    cwd: root,
    env: environment,
    input,
    encoding: "utf8",
  });
  if (result.error) throw result.error;
  return result;
}

function restoreAndBuild(project, config, packages, versionProperty) {
  run("restore", project, "--configfile", config, "--packages", packages, versionProperty);
  run("build", project, "--configuration", "Release", "--no-restore", versionProperty);
}

/** Exit codes and messages of the lender example's offline paths. No request is sent. */
function checkVerifierExample(config, temporary) {
  const project = "examples/verifier/LenderVerifier/LenderVerifier.csproj";
  const version = packageVersion(join(root, "packages/Verifiabl.Verifier/src/Verifiabl.Verifier.csproj"));
  const versionProperty = `--property:VerifiablVerifierVersion=${version}`;
  restoreAndBuild(project, config, join(temporary, "verifier-packages"), versionProperty);

  const withoutCredentials = { ...process.env };
  delete withoutCredentials.VERIFIABL_CLIENT_ID;
  delete withoutCredentials.VERIFIABL_CLIENT_SECRET;
  const withCredentials = {
    ...withoutCredentials,
    VERIFIABL_CLIENT_ID: "package-check-client",
    VERIFIABL_CLIENT_SECRET: "package-check-secret",
  };
  const example = (environment, input, ...arguments_) => capture(
    environment,
    input,
    "run",
    "--project",
    project,
    "--configuration",
    "Release",
    "--no-build",
    versionProperty,
    ...(arguments_.length > 0 ? ["--", ...arguments_] : []),
  );

  const argv = example(withCredentials, "", "payload-in-argv");
  assert.equal(argv.status, 2, "A payload in argv must be refused");
  assert.match(argv.stderr, /Usage: dotnet run < payload\.txt/);

  const missing = example(withoutCredentials, "2|payload\n");
  assert.equal(missing.status, 2, "Missing credentials must be a usage error");
  assert.match(missing.stderr, /VERIFIABL_CLIENT_ID/);

  // An empty payload is rejected before any token or verification request.
  const empty = example(withCredentials, "\r\n");
  assert.equal(empty.status, 1, "An empty payload must be rejected");
  assert.match(empty.stderr, /Not verified \(VALIDATION_FAILED\)/);
  assert.doesNotMatch(empty.stderr, /package-check-secret/);
}

export function check(packageDirectory = "artifacts") {
  const packages = resolve(root, packageDirectory);
  const project = "examples/issuer/SelfManagedIssuer/SelfManagedIssuer.csproj";
  const version = packageVersion(join(root, "packages/Verifiabl.Issuer/src/Verifiabl.csproj"));
  const temporary = mkdtempSync(join(tmpdir(), "verifiabl-dotnet-package-check-"));
  try {
    const config = join(temporary, "NuGet.config");
    writeFileSync(config, packageSourceConfig(packages));
    restoreAndBuild(project, config, join(temporary, "packages"), `--property:VerifiablIssuerVersion=${version}`);
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
      ["PAY-1002", "nz.payslip.v2", "NZD", "9000.00", "paye", "2283.96"],
    ]) {
      const batch = manifest("batch", id);
      assert.equal(batch.verifiablReference, batch.registrationRequest.verifiablReference);
      assert.equal(readFileSync(
        join(output, runDirectory, "batch", id, "xmp-payload.txt"), "utf8",
      ).startsWith(`2|${batch.verifiablReference}|`), true);
      assert.ok(!JSON.stringify(batch).includes("Ana Leota"));
      assert.equal(batch.registrationRequest.schema, schema);
      const fields = batch.registrationRequest.payslipNonPii;
      assert.deepEqual(fields, {
        period_end: "2026-08-31",
        payment_date: "2026-09-04",
        currency,
        gross,
        [taxField]: tax,
        net: id === "PAY-1001" ? "6750.00" : "6716.04",
        ...(id === "PAY-1001" ? { pay_frequency: "monthly" } : {}),
        earnings: [{ type: "ordinary", amount: gross }],
      });
    }
    const single = manifest("single", "PAY-1001").registrationRequest;
    assert.equal(single.schema, "au.payslip.v2");
    assert.deepEqual(single.payslipNonPii, manifest("batch", "PAY-1001").registrationRequest.payslipNonPii);
    assert.notEqual(single.encryptionMetadata.iv, manifest("batch", "PAY-1001").registrationRequest.encryptionMetadata.iv);
    checkVerifierExample(config, temporary);
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) check(process.argv[2]);
