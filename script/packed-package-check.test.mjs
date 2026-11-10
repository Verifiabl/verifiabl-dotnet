import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { metadataPdf, packageVersion, packageSourceConfig } from "./packed-package-check.mjs";

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
