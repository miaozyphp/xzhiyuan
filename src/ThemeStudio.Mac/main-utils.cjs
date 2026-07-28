const path = require("node:path");

function isTrustedExternalUrl(value) {
  try {
    const url = new URL(value);
    return url.protocol === "https:" && url.hostname === "github.com" && url.pathname.startsWith("/miaozyphp/xzhiyuan/");
  } catch {
    return false;
  }
}

function resolveBackendPath(resourcesPath, override) {
  return override || path.join(resourcesPath, "backend", "ThemeStudio.MacBridge");
}

module.exports = { isTrustedExternalUrl, resolveBackendPath };
