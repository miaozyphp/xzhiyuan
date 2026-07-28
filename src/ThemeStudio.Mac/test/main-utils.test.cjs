const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");
const { isTrustedExternalUrl, resolveBackendPath } = require("../main-utils.cjs");

test("only project GitHub links open outside the workbench", () => {
  assert.equal(isTrustedExternalUrl("https://github.com/miaozyphp/xzhiyuan/releases/tag/v0.2.0"), true);
  assert.equal(isTrustedExternalUrl("https://github.com/another/project"), false);
  assert.equal(isTrustedExternalUrl("javascript:alert(1)"), false);
});

test("backend path stays under packaged resources by default", () => {
  assert.equal(resolveBackendPath("/Applications/x.app/Contents/Resources"), path.join("/Applications/x.app/Contents/Resources", "backend", "ThemeStudio.MacBridge"));
  assert.equal(resolveBackendPath("/ignored", "/tmp/custom-bridge"), "/tmp/custom-bridge");
});
