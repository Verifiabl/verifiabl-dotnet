#!/usr/bin/env node

import { spawnSync } from "node:child_process";
import {
  cpSync,
  existsSync,
  mkdtempSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const generatedDirectory = join(root, "generated", "api", "dotnet");

function run(command, args) {
  const result = spawnSync(command, args, { cwd: root, stdio: "inherit" });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(" ")} exited with status ${result.status}`);
  }
}

function filesUnder(directory, base = directory) {
  if (!existsSync(directory)) return [];
  return readdirSync(directory, { withFileTypes: true })
    .flatMap((entry) => {
      const path = join(directory, entry.name);
      return entry.isDirectory() ? filesUnder(path, base) : [relative(base, path)];
    })
    .sort();
}

function validateGeneratedReference(output) {
  const manifestPath = join(output, ".manifest");
  if (!existsSync(join(output, "toc.yml")) || !existsSync(manifestPath)) {
    throw new Error(`DocFX did not create the expected output at ${output}`);
  }

  const uids = Object.keys(JSON.parse(readFileSync(manifestPath, "utf8")));
  const requiredUids = [
    "Verifiabl.VerifiablBarcode",
    "Verifiabl.Client.IVerifiablClient",
    "Verifiabl.Extensions.DependencyInjection.VerifiablServiceCollectionExtensions",
  ];
  const missing = requiredUids.filter((uid) => !uids.includes(uid));
  if (missing.length > 0) throw new Error(`Generated API reference is missing: ${missing.join(", ")}`);

  // Cover non-public implementation types in public namespaces as well as the
  // dedicated internal namespace. DocFX's default filter should exclude all of
  // these; the sentinels make that privacy assumption fail closed if its
  // defaults or our metadata configuration change.
  const forbiddenUidPrefixes = [
    "Verifiabl.Internal",
    "Verifiabl.Pii.FieldOrder",
    "Verifiabl.Pii.FormatV2",
    "Verifiabl.Pii.PayloadMaxBytes",
    "Verifiabl.Pii.TextProfileId",
    "Verifiabl.Pii.TextProfileUnicodeVersion",
    "Verifiabl.PiiV2Fields",
    "Verifiabl.PiiTextProfile",
    "Verifiabl.VerifiablBase32",
    "Verifiabl.Client.VerifiablAuth.ClientCredentialsAuth",
    "Verifiabl.Client.VerifiablClient.CachedToken",
    "Verifiabl.Extensions.DependencyInjection.VerifiablServiceCollectionExtensions.VerifiablClientOptionsValidator",
    "System.Runtime.CompilerServices.IsExternalInit",
    "System.Runtime.CompilerServices.RequiredMemberAttribute",
    "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
  ];
  const nonPublic = uids.filter((uid) =>
    forbiddenUidPrefixes.some((prefix) => uid === prefix || uid.startsWith(`${prefix}.`)),
  );
  if (nonPublic.length > 0) {
    throw new Error(`Generated API reference exposes non-public APIs: ${nonPublic.join(", ")}`);
  }
}

function generate(destinationBase) {
  run("dotnet", ["tool", "restore"]);
  run("dotnet", ["restore", "--locked-mode"]);
  run("dotnet", [
    "docfx",
    "metadata",
    "docs/docfx.json",
    "--warningsAsErrors",
    "--output",
    join(destinationBase, "docs"),
  ]);

  const output = join(destinationBase, "generated", "api", "dotnet");
  validateGeneratedReference(output);
  return output;
}

const check = process.argv[2] === "--check";
if (process.argv.length > (check ? 3 : 2)) {
  console.error(`Usage: ${process.argv[1]} [--check]`);
  process.exit(2);
}

const temporaryDirectory = mkdtempSync(join(tmpdir(), "verifiabl-dotnet-api-"));
try {
  mkdirSync(join(temporaryDirectory, "docs"), { recursive: true });
  const freshDirectory = generate(temporaryDirectory);

  if (check) {
    console.log(`Validated ${filesUnder(freshDirectory).length} .NET API reference files.`);
  } else {
    rmSync(generatedDirectory, { recursive: true, force: true });
    mkdirSync(dirname(generatedDirectory), { recursive: true });
    cpSync(freshDirectory, generatedDirectory, { recursive: true });
    console.log(`Generated ${filesUnder(generatedDirectory).length} .NET API reference files.`);
  }
} finally {
  rmSync(temporaryDirectory, { recursive: true, force: true });
}
