import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { issuerVersion, packageSourceConfig } from "./packed-package-check.mjs";

test("reads the package version from the issuer project", () => {
  const directory = mkdtempSync(join(tmpdir(), "verifiabl-version-test-"));
  try {
    const project = join(directory, "issuer.csproj");
    writeFileSync(project, "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>\n");
    assert.equal(issuerVersion(project), "1.2.3");
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
