import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { blankPng, metadataPdf, packageVersion, packageSourceConfig, wasmtimeLibraries } from "./packed-package-check.mjs";

test("reads the package version from a project", () => {
  const directory = mkdtempSync(join(tmpdir(), "verifiabl-version-test-"));
  try {
    const project = join(directory, "issuer.csproj");
    writeFileSync(project, "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>\n");
    assert.equal(packageVersion(project), "1.2.3");
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("limits Verifiabl packages to the packed source", () => {
  const config = packageSourceConfig("/tmp/packages");
  assert.match(config, /key="packed" value="\/tmp\/packages"/);
  assert.match(config, /packageSource key="packed"><package pattern="Verifiabl\.\*"/);
  assert.match(config, /packageSource key="nuget\.org"><package pattern="\*"/);
});

test("writes a PDF whose cross-reference offsets point at its objects", () => {
  const pdf = metadataPdf("2|reference|ciphertext").toString("utf8");
  const xref = Number(pdf.match(/startxref\n(\d+)/)[1]);
  assert.ok(pdf.startsWith("xref", xref));
  const offsets = [...pdf.slice(xref).matchAll(/^(\d{10}) 00000 n $/gm)].map((match) => Number(match[1]));
  assert.equal(offsets.length, 4);
  offsets.forEach((offset, index) => assert.ok(pdf.startsWith(`${index + 1} 0 obj`, offset)));
  assert.match(pdf, /<verifiabl:payload>2\|reference\|ciphertext<\/verifiabl:payload>/);
});

test("writes a PNG whose chunk checksums match", () => {
  const png = blankPng(4);
  assert.deepEqual([...png.subarray(0, 8)], [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
  // IHDR's CRC over its type and data for a 4x4 8-bit greyscale image, as zlib.crc32 computes it.
  assert.equal(png.readUInt32BE(29), 0x8c9ac1a2);
});

test("names each pinned Wasmtime library where NuGet looks for it", () => {
  const props = `<Project><ItemGroup>
    <WasmtimeArchive Include="x86_64-windows" RuntimeIdentifier="win-x64" Format="zip" Library="wasmtime.dll"
                     Sha256="00" />
    <WasmtimeArchive Include="aarch64-macos" RuntimeIdentifier="osx-arm64" Format="tar.xz" Library="libwasmtime.dylib" Sha256="11" />
  </ItemGroup></Project>`;
  assert.deepEqual(wasmtimeLibraries(props), [
    "runtimes/win-x64/native/wasmtime.dll",
    "runtimes/osx-arm64/native/libwasmtime.dylib",
  ]);
});
