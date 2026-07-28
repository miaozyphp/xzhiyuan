const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("xzhiyuan", {
  postMessage(message) {
    ipcRenderer.send("bridge-request", message);
  },
  addEventListener(name, callback) {
    if (name !== "message" || typeof callback !== "function") return;
    ipcRenderer.on("bridge-message", (_event, data) => callback({ data }));
  }
});
