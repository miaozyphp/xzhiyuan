const path = require("node:path");

const required = name => {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required.`);
  return path.resolve(value);
};

module.exports = {
  appId: "com.xzhiyuan.theme-studio",
  productName: "x纸鸢",
  asar: true,
  files: ["main.cjs", "main-utils.cjs", "preload.cjs", "package.json"],
  extraResources: [
    { from: required("XZHIYUAN_MAC_BACKEND_DIR"), to: "backend" },
    { from: required("XZHIYUAN_UI_DIR"), to: "ui" },
    { from: required("XZHIYUAN_SEED_DIR"), to: "SeedAssets" }
  ],
  directories: { output: required("XZHIYUAN_MAC_OUTPUT_DIR") },
  mac: {
    category: "public.app-category.developer-tools",
    icon: required("XZHIYUAN_MAC_ICON"),
    identity: null,
    hardenedRuntime: false,
    gatekeeperAssess: false,
    artifactName: "XZhiYuan-${version}-macos-${arch}.${ext}"
  },
  dmg: {
    artifactName: "XZhiYuan-Setup-${version}-macos-${arch}.${ext}",
    title: "x纸鸢 ${version}",
    sign: false
  }
};
