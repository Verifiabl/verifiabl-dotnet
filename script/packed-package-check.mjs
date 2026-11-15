#!/usr/bin/env node

import { accessSync, chmodSync, constants, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import assert from "node:assert/strict";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { deflateSync } from "node:zlib";

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

/** Exit codes and messages of the payload verifier example's offline paths. No request is sent. */
function checkVerifierExample(config, temporary) {
  const project = "examples/verifier/PayloadVerifier/PayloadVerifier.csproj";
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
  assert.match(empty.stderr, /Not verified \(NOT_VERIFIED\)/);
  assert.doesNotMatch(empty.stderr, /package-check-secret/);
}

/**
 * A one-page PDF whose XMP metadata carries the payload, as an issuer's PDF
 * tool writes the metadata copy. It has no images, so it reads as metadata only.
 */
export function metadataPdf(payload) {
  const xmp = [
    '<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">',
    `<rdf:Description rdf:about="" xmlns:verifiabl="https://verifiabl.io/ns/"><verifiabl:payload>${payload}</verifiabl:payload></rdf:Description>`,
    "</rdf:RDF></x:xmpmeta>",
  ].join("\n");
  const objects = [
    "<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>",
    "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>",
    `<< /Type /Metadata /Subtype /XML /Length ${Buffer.byteLength(xmp)} >>\nstream\n${xmp}\nendstream`,
  ];
  let pdf = "%PDF-1.7\n";
  const offsets = objects.map((object, index) => {
    const offset = Buffer.byteLength(pdf);
    pdf += `${index + 1} 0 obj\n${object}\nendobj\n`;
    return offset;
  });
  const xref = Buffer.byteLength(pdf);
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  pdf += offsets.map((offset) => `${String(offset).padStart(10, "0")} 00000 n \n`).join("");
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(pdf, "utf8");
}

/** PNG's CRC-32. zlib.crc32 needs a newer Node than some CI runners carry. */
function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = crc & 1 ? (crc >>> 1) ^ 0xedb88320 : crc >>> 1;
  }
  return (crc ^ 0xffffffff) >>> 0;
}

/** A white greyscale PNG with no QR code, so reading it exercises the sandboxed decoder. */
export function blankPng(size = 64) {
  const chunk = (type, data) => {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(body));
    return Buffer.concat([length, body, crc]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8; // bit depth
  header[9] = 0; // greyscale
  // Each row is a filter byte of 0 followed by white pixels.
  const row = Buffer.concat([Buffer.from([0]), Buffer.alloc(size, 255)]);
  const pixels = Buffer.concat(Array.from({ length: size }, () => row));
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", header),
    chunk("IDAT", deflateSync(pixels)),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

function isReadable(path) {
  try {
    accessSync(path, constants.R_OK);
    return true;
  } catch {
    return false;
  }
}

/** Exit codes and messages of the document example's offline paths. No request is sent. */
function checkDocumentExample(config, temporary, xmpPayload) {
  const project = "examples/verifier/DocumentVerifier/DocumentVerifier.csproj";
  const version = packageVersion(join(root, "packages/Verifiabl.Verifier.Extraction/src/Verifiabl.Verifier.Extraction.csproj"));
  const versionProperty = `--property:VerifiablVerifierVersion=${version}`;
  restoreAndBuild(project, config, join(temporary, "document-packages"), versionProperty);

  const environment = { ...process.env };
  delete environment.VERIFIABL_CLIENT_ID;
  delete environment.VERIFIABL_CLIENT_SECRET;
  const example = (...arguments_) => capture(
    environment,
    "",
    "run",
    "--project",
    project,
    "--configuration",
    "Release",
    "--no-build",
    versionProperty,
    "--",
    ...arguments_,
  );
  const pdf = join(temporary, "payslip.pdf");
  writeFileSync(pdf, metadataPdf(xmpPayload));

  const unknown = example("tiff", pdf);
  assert.equal(unknown.status, 2, "An unknown command must be a usage error");
  assert.match(unknown.stderr, /pdf\s+A payslip PDF/);

  assert.equal(example("pdf", join(temporary, "missing.pdf")).status, 2, "A missing file must be a usage error");

  // An existing file that cannot be opened. Root can read it anyway, so the check runs only when the mode applies.
  const unreadable = join(temporary, "unreadable.pdf");
  writeFileSync(unreadable, metadataPdf(xmpPayload));
  chmodSync(unreadable, 0o000);
  if (!isReadable(unreadable)) {
    const locked = example("pdf", unreadable, "--read-only");
    assert.equal(locked.status, 2, `An unreadable file must be reported, not crash: ${locked.stderr}`);
    assert.match(locked.stderr, /Could not read/);
  }

  const missing = example("pdf", pdf);
  assert.equal(missing.status, 2, "Verifying without credentials must be a usage error");
  assert.match(missing.stderr, /--read-only/);

  // The issuer example's real payload, read back from PDF metadata by the verifier add-on.
  const read = example("pdf", pdf, "--read-only");
  assert.equal(read.status, 0, `Read-only mode must read the metadata payload: ${read.stderr}`);
  assert.match(read.stdout, /HasMetadata = True, HasQrCode = False, RenderedPages = 0/);
  // The reference may be printed; the ciphertext must not be.
  const output = read.stdout + read.stderr;
  const ciphertext = xmpPayload.split("|").at(-1);
  assert.ok(!output.includes(xmpPayload), "Read-only mode must not print the payload");
  assert.ok(!output.includes(ciphertext), "Read-only mode must not print the ciphertext");

  // Decoding needs Wasmtime's native library and the embedded module from the packed packages.
  const image = join(temporary, "blank.png");
  writeFileSync(image, blankPng());
  const blank = example("image", image, "--read-only");
  assert.equal(blank.status, 1, `An image without a QR code must not be read as one: ${blank.stderr}`);
  assert.match(blank.stdout, /HasQrCode = False/);

  const notImage = example("image", pdf, "--read-only");
  assert.equal(notImage.status, 1, `A file that is not an image must be reported: ${notImage.stderr}`);
  assert.match(notImage.stderr, /Could not read a payload \(Unreadable\)/);
}

/** Where NuGet looks for each Wasmtime library that Wasmtime.props pins. */
export function wasmtimeLibraries(props) {
  const archives = props.matchAll(/<WasmtimeArchive\b[^>]*\bRuntimeIdentifier="([^"]+)"[^>]*\bLibrary="([^"]+)"/g);
  return Array.from(archives, ([, runtime, library]) => `runtimes/${runtime}/native/${library}`);
}

// The Extraction package carries other projects' code, so their licences must be in it,
// with the record of the module's release that its README cites. The example above
// proves only this machine's Wasmtime library.
function checkExtractionContents(packages) {
  const version = packageVersion(join(root, "packages/Verifiabl.Verifier.Extraction/src/Verifiabl.Verifier.Extraction.csproj"));
  const archive = readFileSync(join(packages, `Verifiabl.Verifier.Extraction.${version}.nupkg`));
  // A zip archive stores each file's name as plain bytes.
  const libraries = wasmtimeLibraries(readFileSync(join(root, "packages/Verifiabl.Verifier.Extraction/src/Wasmtime.props"), "utf8"));
  assert.ok(libraries.length > 0, "Wasmtime.props must pin a library");
  // NuGet imports build targets only from a file named after the package.
  const targets = "buildTransitive/net472/Verifiabl.Verifier.Extraction.targets";
  for (const file of ["THIRD-PARTY-NOTICES.txt", "extraction-release.json", "licenses/wasmtime/LICENSE", targets, ...libraries]) {
    assert.ok(archive.includes(file), `Verifiabl.Verifier.Extraction must pack ${file}`);
  }
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
    const xmpPayload = readFileSync(
      join(output, runDirectory, "batch", "PAY-1001", "xmp-payload.txt"), "utf8",
    ).trim();
    checkDocumentExample(config, temporary, xmpPayload);
    checkExtractionContents(packages);
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) check(process.argv[2]);
