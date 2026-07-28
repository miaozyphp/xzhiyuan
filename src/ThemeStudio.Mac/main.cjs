const { app, BrowserWindow, Menu, Tray, dialog, ipcMain, nativeImage, shell } = require("electron");
const { spawn } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");
const readline = require("node:readline");
const { isTrustedExternalUrl, resolveBackendPath } = require("./main-utils.cjs");

app.setName("x纸鸢");
const hasSingleInstanceLock = app.requestSingleInstanceLock();
if (!hasSingleInstanceLock) app.quit();

let mainWindow = null;
let tray = null;
let backend = null;
let quitting = false;
let brokerEnabled = false;
let brokerPending = false;
let brokerTimer = null;
let nextInternalId = 0;
const rendererMethods = new Map();
const internalRequests = new Map();
const resourcesRoot = process.env.XZHIYUAN_RESOURCES_DIR || process.resourcesPath;

function resourcePath(...segments) {
  return path.join(resourcesRoot, ...segments);
}

function rendererSend(message) {
  if (mainWindow && !mainWindow.isDestroyed()) mainWindow.webContents.send("bridge-message", message);
}

function writeBackend(message) {
  if (!backend?.stdin?.writable) throw new Error("x纸鸢后端尚未就绪。");
  backend.stdin.write(`${JSON.stringify(message)}\n`);
}

function requestBackend(method, params = {}) {
  const id = `host-${++nextInternalId}`;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      internalRequests.delete(id);
      reject(new Error("后台操作等待时间过长，请重试。"));
    }, method === "downloadUpdate" ? 900000 : 120000);
    internalRequests.set(id, { resolve, reject, timer, method });
    try {
      writeBackend({ id, method, params });
    } catch (error) {
      clearTimeout(timer);
      internalRequests.delete(id);
      reject(error);
    }
  });
}

async function handleBackendMessage(message) {
  if (message?.event) {
    rendererSend(message);
    return;
  }

  const internal = internalRequests.get(message?.id);
  if (internal) {
    clearTimeout(internal.timer);
    internalRequests.delete(message.id);
    if (internal.method === "brokerTick") brokerPending = false;
    if (message.ok) internal.resolve(message.result);
    else internal.reject(new Error(message.error || "后台操作失败。"));
    return;
  }

  const method = rendererMethods.get(message?.id);
  rendererMethods.delete(message?.id);
  if (message.ok && method === "bootstrap") configureAutoApply(Boolean(message.result?.settings?.brokerEnabled));
  if (message.ok && method === "setAutoApply") configureAutoApply(Boolean(message.result?.brokerEnabled));
  if (message.ok && method === "installUpdate") {
    const installerPath = message.result?.installerPath;
    const openError = installerPath ? await shell.openPath(installerPath) : "没有找到已下载的更新包。";
    if (openError) message = { id: message.id, ok: false, result: null, error: openError };
    else message = { id: message.id, ok: true, result: { opened: true }, error: null };
  }
  rendererSend(message);
}

function startBackend() {
  const executable = resolveBackendPath(process.resourcesPath, process.env.XZHIYUAN_MAC_BRIDGE);
  if (!fs.existsSync(executable)) throw new Error(`缺少后端组件：${executable}`);
  backend = spawn(executable, [
    "--data-root", app.getPath("userData"),
    "--resources-root", resourcesRoot
  ], {
    stdio: ["pipe", "pipe", "ignore"],
    windowsHide: true
  });

  readline.createInterface({ input: backend.stdout }).on("line", line => {
    try { void handleBackendMessage(JSON.parse(line)); }
    catch { }
  });
  backend.on("error", error => rendererSend({ event: "fatalError", data: { message: error.message } }));
  backend.on("exit", code => {
    backend = null;
    if (!quitting) rendererSend({ event: "fatalError", data: { message: `x纸鸢后端已停止（${code ?? "未知"}）。` } });
  });
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1480,
    height: 900,
    minWidth: 1180,
    minHeight: 720,
    show: false,
    title: "x纸鸢",
    backgroundColor: "#101416",
    icon: resourcePath("SeedAssets", "x-zhiyuan-emblem.png"),
    webPreferences: {
      preload: path.join(__dirname, "preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
      devTools: false
    }
  });

  mainWindow.removeMenu();
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (isTrustedExternalUrl(url)) void shell.openExternal(url);
    return { action: "deny" };
  });
  mainWindow.webContents.on("will-navigate", (event, url) => {
    if (url !== mainWindow.webContents.getURL()) event.preventDefault();
  });
  mainWindow.on("close", event => {
    if (quitting) return;
    event.preventDefault();
    mainWindow.hide();
  });
  mainWindow.once("ready-to-show", () => {
    if (!process.argv.includes("--broker")) mainWindow.show();
  });
  mainWindow.webContents.once("did-finish-load", () => {
    if (!process.argv.includes("--broker")) mainWindow.show();
  });

  const uiPath = process.env.XZHIYUAN_UI_DIR
    ? path.join(process.env.XZHIYUAN_UI_DIR, "index.html")
    : resourcePath("ui", "index.html");
  void mainWindow.loadFile(uiPath);
}

function createTray() {
  const source = nativeImage.createFromPath(resourcePath("SeedAssets", "x-zhiyuan-emblem.png"));
  const image = source.resize({ width: 18, height: 18 });
  tray = new Tray(image);
  tray.setToolTip("x纸鸢");
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: "打开 x纸鸢", click: showWindow },
    { label: "启动 Codex", click: () => void requestBackend("launchCodex").catch(() => {}) },
    { type: "separator" },
    { label: "退出 x纸鸢", click: quitApplication }
  ]));
  tray.on("click", showWindow);
}

function showWindow() {
  if (!mainWindow) return;
  mainWindow.show();
  mainWindow.focus();
}

function configureAutoApply(enabled) {
  brokerEnabled = enabled;
  if (process.platform === "darwin") {
    app.setLoginItemSettings({ openAtLogin: enabled, openAsHidden: true, args: ["--broker"] });
  }
}

function startBrokerTimer() {
  brokerTimer = setInterval(() => {
    if (!brokerEnabled || brokerPending || !backend) return;
    brokerPending = true;
    void requestBackend("brokerTick").catch(() => { brokerPending = false; });
  }, 1500);
}

async function pickAsset(message, badgeOnly) {
  const filters = badgeOnly
    ? [{ name: "图片", extensions: ["png", "jpg", "jpeg", "webp", "gif"] }]
    : [
        { name: "图片和视频", extensions: ["png", "jpg", "jpeg", "webp", "gif", "mp4", "webm", "mov"] },
        { name: "图片", extensions: ["png", "jpg", "jpeg", "webp", "gif"] },
        { name: "视频", extensions: ["mp4", "webm", "mov"] }
      ];
  const result = await dialog.showOpenDialog(mainWindow, { properties: ["openFile"], filters });
  if (result.canceled || !result.filePaths[0]) return { cancelled: true };
  return requestBackend("importAsset", { themeId: message.params?.themeId || "custom-theme", sourcePath: result.filePaths[0] });
}

ipcMain.on("bridge-request", (_event, message) => {
  if (!message?.id || !message?.method) return;
  if (message.method === "pickMedia" || message.method === "pickBadge") {
    void pickAsset(message, message.method === "pickBadge")
      .then(result => rendererSend({ id: message.id, ok: true, result, error: null }))
      .catch(error => rendererSend({ id: message.id, ok: false, result: null, error: error.message }));
    return;
  }
  rendererMethods.set(message.id, message.method);
  try { writeBackend(message); }
  catch (error) {
    rendererMethods.delete(message.id);
    rendererSend({ id: message.id, ok: false, result: null, error: error.message });
  }
});

function quitApplication() {
  quitting = true;
  app.quit();
}

app.on("second-instance", () => showWindow());
app.on("activate", () => showWindow());
app.on("before-quit", () => { quitting = true; });

app.whenReady().then(() => {
  try {
    startBackend();
    createWindow();
    createTray();
    startBrokerTimer();
  } catch (error) {
    dialog.showErrorBox("x纸鸢没有启动成功", error.message);
    quitApplication();
  }
});

app.on("quit", () => {
  if (brokerTimer) clearInterval(brokerTimer);
  for (const pending of internalRequests.values()) clearTimeout(pending.timer);
  internalRequests.clear();
  try { backend?.stdin?.end(); } catch { }
  try { backend?.kill(); } catch { }
  tray?.destroy();
});
