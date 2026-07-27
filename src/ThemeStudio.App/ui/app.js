(() => {
  "use strict";

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
  const clone = value => JSON.parse(JSON.stringify(value));
  const pending = new Map();
  const maxDroppedMediaBytes = 80 * 1024 * 1024;
  let requestNumber = 0;

  const state = {
    themes: [],
    settings: {},
    status: {},
    selectedId: null,
    original: null,
    draft: null,
    draftMediaUrl: null,
    draftBadgeUrl: null,
    activeFilter: "all",
    collapsedSections: new Set(),
    saveIntent: "copy",
    activeInspector: "appearance",
    activePreview: "home",
    dirty: false,
    deleteId: null,
    busy: false,
    busyButton: null
  };

  const bridgeAvailable = Boolean(window.chrome?.webview);

  function api(method, params = {}) {
    if (!bridgeAvailable) return mockApi(method, params);
    const id = `request-${Date.now()}-${++requestNumber}`;
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject });
      window.chrome.webview.postMessage({ id, method, params });
      window.setTimeout(() => {
        const waiting = pending.get(id);
        if (!waiting) return;
        pending.delete(id);
        waiting.reject(new Error("操作等待时间过长，请重试。"));
      }, method === "applyTheme" || method === "restartAndApply" ? 120000 : 30000);
    });
  }

  if (bridgeAvailable) {
    window.chrome.webview.addEventListener("message", event => {
      const message = event.data;
      if (message?.event === "runtimeStatus") {
        state.status = message.data;
        renderStatus();
        return;
      }
      const waiting = pending.get(message?.id);
      if (!waiting) return;
      pending.delete(message.id);
      if (message.ok) waiting.resolve(message.result);
      else waiting.reject(new Error(message.error || "操作没有完成。"));
    });
  }

  async function mockApi(method, params) {
    if (method !== "bootstrap" && method !== "refresh") {
      await new Promise(resolve => setTimeout(resolve, 220));
      if (method === "applyTheme" || method === "launchCodex") return { success: true, message: "预览模式：皮肤已准备。", suspendedLayers: [] };
      if (method === "pickMedia" || method === "pickBadge") return { cancelled: true };
      if (method === "createThemeCopy") {
        const theme = { ...clone(params.theme), id: `custom-${Date.now().toString(36)}`, name: params.name, builtIn: false, updatedAt: new Date().toISOString() };
        return { theme, mediaUrl: state.draftMediaUrl, badgeUrl: state.draftBadgeUrl };
      }
      if (method === "createThemeFromDroppedAsset") {
        const isVideo = /\.(mp4|webm|mov)$/i.test(params.fileName || "");
        const theme = {
          ...clone(params.theme),
          id: `custom-${Date.now().toString(36)}`,
          name: params.name || "新建主题",
          builtIn: false,
          updatedAt: new Date().toISOString(),
          media: { ...clone(params.theme.media), kind: isVideo ? "video" : "image", assetPath: null }
        };
        return { theme, mediaUrl: params.dataUrl, badgeUrl: state.draftBadgeUrl };
      }
      if (method === "duplicateTheme") {
        const source = state.themes.find(item => item.theme.id === params.id);
        const theme = { ...clone(source?.theme), id: `custom-${Date.now().toString(36)}`, name: params.name, builtIn: false, updatedAt: new Date().toISOString() };
        return { theme, mediaUrl: source?.mediaUrl || null, badgeUrl: source?.badgeUrl || null };
      }
      if (method === "saveTheme") return { theme: { ...clone(params.theme), updatedAt: new Date().toISOString() }, mediaUrl: state.draftMediaUrl, badgeUrl: state.draftBadgeUrl };
      return params;
    }
    const sample = {
      schemaVersion: 1, id: "rain-archive", name: "雨夜档案馆", description: "冷雨、黑石与温暖阅览灯。", version: "1.0.3", builtIn: true,
      mode: "deep", updatedAt: new Date().toISOString(),
      palette: { canvas: "#0B1013", surface: "#141B1F", elevated: "#1C272D", text: "#F1F4F3", mutedText: "#A7B2B6", border: "#334047", accent: "#35B8CE", accentText: "#061215", success: "#44B982", warning: "#D6A64B", danger: "#E66B62" },
      media: { kind: "image", assetPath: null, opacity: .92, blur: 0, fit: "cover", position: "center", muted: true },
      surfaces: { opacity: .22, blur: 6, radius: 6, sidebarOpacity: .18, composerOpacity: .72, bubbleOpacity: .82 },
      layers: { media: true, surfaces: true, components: true, badge: true, hero: true, suggestions: true, homeLayout: true },
      badge: { assetPath: "assets/built-in/theme-studio-emblem.png", text: "X", position: "top-left", style: "icon", size: 24, offsetX: 8, offsetY: 6, radius: 6, opacity: .95, backgroundOpacity: .82, borderOpacity: .35 }
    };
    const variants = [
      sample,
      { ...clone(sample), id: "paper-sky", name: "纸上晴空", mode: "standard", media: { ...sample.media, kind: "none" }, palette: { ...sample.palette, canvas: "#EAF2F3", surface: "#F8FBFA", elevated: "#FFFFFF", text: "#142023", mutedText: "#66777C", border: "#C5D3D6", accent: "#168DA3", accentText: "#FFFFFF" } },
      { ...clone(sample), id: "amber-library", name: "琥珀图书馆", media: { ...sample.media, kind: "none" }, palette: { ...sample.palette, canvas: "#17130E", surface: "#211A12", elevated: "#2A2117", text: "#F4EBDD", mutedText: "#BDAE99", border: "#493B2A", accent: "#D79A36", accentText: "#1B1207" } }
    ];
    return { themes: variants.map(theme => ({ theme, mediaUrl: theme.id === "rain-archive" ? "../SeedAssets/rain-archive.png" : null, badgeUrl: "assets/x-zhiyuan-emblem.png" })), settings: { defaultThemeId: "rain-archive", brokerEnabled: false }, status: { state: "codexStopped", message: "Codex 已就绪", codexVersion: "26.721" } };
  }

  async function initialize() {
    bindStaticEvents();
    refreshIcons();
    try {
      const data = await api("bootstrap");
      applyBootstrap(data);
      $("#app-shell").classList.remove("is-loading");
    } catch (error) {
      $("#app-shell").classList.remove("is-loading");
      toast("工作台未加载", error.message, "error");
    }
  }

  function applyBootstrap(data) {
    state.themes = data.themes || [];
    state.settings = data.settings || {};
    state.status = data.status || {};
    $("#auto-apply-toggle").checked = Boolean(state.settings.brokerEnabled);
    renderStatus();
    renderThemes();
    const preferred = state.themes.find(item => item.theme.id === state.settings.defaultThemeId) || state.themes[0];
    if (preferred) selectTheme(preferred.theme.id);
  }

  function bindStaticEvents() {
    $("#theme-search").addEventListener("input", renderThemes);
    $("#filter-row").addEventListener("click", event => {
      const button = event.target.closest("[data-filter]");
      if (!button) return;
      activateThemeFilter(button.dataset.filter);
    });
    $("#theme-list").addEventListener("click", onThemeListClick);
    $("#refresh-button").addEventListener("click", refreshWorkbench);
    $("#save-copy-button").addEventListener("click", openSaveDialog);
    $("#new-theme-button").addEventListener("click", openNewThemeDialog);
    $("#update-button").addEventListener("click", updateCurrentTheme);
    $("#launch-button").addEventListener("click", event => applyCurrentTheme("正在启动 Codex", event.currentTarget));
    $("#apply-button").addEventListener("click", event => applyCurrentTheme("正在应用皮肤", event.currentTarget));
    $("#wide-apply-button").addEventListener("click", event => applyCurrentTheme("正在应用皮肤", event.currentTarget));
    $("#quick-media-button").addEventListener("click", () => chooseAsset("media"));
    $("#reset-button").addEventListener("click", resetDraft);
    $("#theme-name").addEventListener("input", event => updateDraft("name", event.target.value));

    $("#auto-apply-toggle").addEventListener("change", async event => {
      const enabled = event.target.checked;
      try {
        const settings = await api("setAutoApply", { enabled });
        state.settings = settings;
        toast(enabled ? "已开启自动应用" : "已关闭自动应用", enabled ? "直接打开 Codex 时会载入默认主题。" : "Codex 将使用普通启动方式。", "success");
      } catch (error) {
        event.target.checked = !enabled;
        toast("设置未保存", error.message, "error");
      }
    });

    $$(".preview-tab").forEach(button => button.addEventListener("click", () => setPreviewView(button.dataset.view)));
    $$(".inspector-tab").forEach(button => button.addEventListener("click", () => setInspectorTab(button.dataset.tab)));
    $$(".mode-switch button").forEach(button => button.addEventListener("click", () => {
      if (!state.draft) return;
      state.draft.mode = button.dataset.mode;
      markDirty();
      renderMode();
      renderInspector();
      applyPreview();
    }));
    $$(".region-legend button").forEach(button => {
      button.addEventListener("mouseenter", () => pulseRegion(button.dataset.regionTarget, true));
      button.addEventListener("mouseleave", () => pulseRegion(button.dataset.regionTarget, false));
      button.addEventListener("click", () => pulseRegion(button.dataset.regionTarget, true, true));
    });

    $("#save-form").addEventListener("submit", saveCopy);
    $("#cancel-save").addEventListener("click", () => $("#save-dialog").close());
    $("#delete-form").addEventListener("submit", deleteTheme);
    $("#cancel-delete").addEventListener("click", () => $("#delete-dialog").close());
    $("#reconnect-form").addEventListener("submit", restartAndApply);
    $("#cancel-reconnect").addEventListener("click", () => $("#reconnect-dialog").close());
    document.addEventListener("click", createRipple);

    const dropZone = $("#library-drop-zone");
    ["dragenter", "dragover"].forEach(type => dropZone.addEventListener(type, event => {
      event.preventDefault();
      if (!state.busy) dropZone.classList.add("is-dragover");
    }));
    dropZone.addEventListener("dragleave", event => {
      if (!dropZone.contains(event.relatedTarget)) dropZone.classList.remove("is-dragover");
    });
    dropZone.addEventListener("drop", handleMediaDrop);
  }

  async function handleMediaDrop(event) {
    event.preventDefault();
    const dropZone = $("#library-drop-zone");
    dropZone.classList.remove("is-dragover");
    if (state.busy || !state.draft) return;
    const files = [...(event.dataTransfer?.files || [])];
    if (!files.length) {
      toast("没有找到媒体文件", "请拖入图片或视频文件。", "info");
      return;
    }

    const queued = [];
    const failures = [];
    files.forEach(file => {
      if (!isSupportedMediaFile(file)) {
        failures.push({ file, reason: "格式不支持" });
      } else if (file.size > maxDroppedMediaBytes) {
        failures.push({ file, reason: "超过 80 MB" });
      } else {
        queued.push(file);
      }
    });
    if (!queued.length) {
      toast("没有可导入的文件", describeDroppedMediaFailures(failures), "error");
      return;
    }

    const sourceTheme = clone(state.draft);
    const usedNames = new Set(state.themes.map(item => normalizeThemeName(item.theme.name)));
    const created = [];
    setBusy(true, `准备导入 ${queued.length} 个主题`);
    try {
      for (let index = 0; index < queued.length; index += 1) {
        const file = queued[index];
        setBusy(true, `正在导入 ${index + 1}/${queued.length}：${shortFileName(file.name)}`);
        try {
          const dataUrl = await readFileAsDataUrl(file);
          const name = createUniqueThemeName(themeNameFromFile(file.name), usedNames);
          const item = await api("createThemeFromDroppedAsset", {
            theme: sourceTheme,
            name,
            fileName: file.name,
            dataUrl
          });
          state.themes.push(item);
          created.push(item);
          usedNames.add(normalizeThemeName(item.theme.name));
        } catch (error) {
          failures.push({ file, reason: error.message || "导入失败" });
        }
      }

      if (created.length) {
        showCustomTheme(created.at(-1).theme.id);
        const skipped = failures.length;
        const title = created.length === 1 && !skipped ? "自定义主题已创建" : "批量导入已完成";
        const message = skipped
          ? `成功 ${created.length} 个，跳过 ${skipped} 个。${describeDroppedMediaFailures(failures)}`
          : `已新建 ${created.length} 个自定义主题，并按图片和视频自动归类。`;
        toast(title, message, skipped ? "info" : "success");
      } else {
        toast("批量导入失败", describeDroppedMediaFailures(failures), "error");
      }
    } finally {
      setBusy(false);
    }
  }

  function isSupportedMediaFile(file) {
    return /\.(png|jpe?g|webp|gif|mp4|webm|mov)$/i.test(file.name || "");
  }

  function themeNameFromFile(fileName) {
    return (fileName || "").replace(/\.[^.]+$/, "").trim().slice(0, 80).trim() || "新建主题";
  }

  function normalizeThemeName(name) {
    return String(name || "").trim().toLocaleLowerCase();
  }

  function createUniqueThemeName(baseName, usedNames) {
    let sequence = 1;
    let candidate = baseName;
    while (usedNames.has(normalizeThemeName(candidate))) {
      sequence += 1;
      const suffix = ` (${sequence})`;
      candidate = `${baseName.slice(0, 80 - suffix.length).trimEnd()}${suffix}`;
    }
    return candidate;
  }

  function shortFileName(fileName) {
    const value = String(fileName || "未命名文件");
    return value.length > 26 ? `${value.slice(0, 23)}...` : value;
  }

  function describeDroppedMediaFailures(failures) {
    if (!failures.length) return "";
    const details = failures.slice(0, 2).map(({ file, reason }) => `“${shortFileName(file?.name)}”${reason}`).join("；");
    const remaining = failures.length - 2;
    return remaining > 0 ? `${details}；另有 ${remaining} 个文件未导入。` : `${details}。`;
  }

  function readFileAsDataUrl(file) {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result || ""));
      reader.onerror = () => reject(new Error("文件读取失败，请重试。"));
      reader.readAsDataURL(file);
    });
  }

  function renderThemes() {
    const search = $("#theme-search").value.trim().toLocaleLowerCase();
    const visible = state.themes.filter(item => {
      const theme = item.theme;
      const matchesSearch = !search || `${theme.name} ${theme.description || ""}`.toLocaleLowerCase().includes(search);
      if (!matchesSearch) return false;
      if (state.activeFilter === "custom") return !theme.builtIn;
      if (state.activeFilter === "light") return colorLuminance(theme.palette.canvas) > .55;
      if (state.activeFilter === "warm") return isWarm(theme.palette.accent);
      return true;
    });

    $("#theme-count").textContent = state.themes.length;
    const list = $("#theme-list");
    list.replaceChildren();
    if (!visible.length) {
      const empty = document.createElement("div");
      empty.className = "library-empty";
      empty.textContent = "没有符合条件的主题";
      list.append(empty);
      return;
    }

    const groupOrder = item => item.theme.builtIn ? 2 : mediaKind(item.theme.media.kind) === "video" ? 1 : 0;
    const orderedVisible = visible.slice().sort((left, right) => groupOrder(left) - groupOrder(right));
    orderedVisible.forEach((item, index) => {
      const theme = item.theme;
      const isDefault = theme.id === state.settings.defaultThemeId;
      const selectedDraft = theme.id === state.selectedId ? state.draft : null;
      const thumbMediaUrl = selectedDraft ? state.draftMediaUrl : item.mediaUrl;
      const thumbMediaKind = mediaKind(selectedDraft ? selectedDraft.media.kind : theme.media.kind);
      const thumbMedia = thumbMediaUrl && thumbMediaKind === "image"
        ? `<img src="${attr(thumbMediaUrl)}" alt="">`
        : thumbMediaUrl && thumbMediaKind === "video"
          ? `<video class="video-cover" src="${attr(thumbMediaUrl)}" muted playsinline preload="metadata" aria-hidden="true"></video><span class="video-cover-mark"><i data-lucide="film"></i></span>`
          : "";
      const row = document.createElement("div");
      row.className = `theme-item${theme.id === state.selectedId ? " active" : ""}`;
      row.dataset.section = theme.builtIn ? "default" : mediaKind(theme.media.kind) === "video" ? "custom-video" : "custom-image";
      row.style.setProperty("--item-index", index);
      row.innerHTML = `
        <button class="theme-select" type="button" data-select="${attr(theme.id)}">
          <span class="theme-thumb" style="--thumb-canvas:${attr(theme.palette.canvas)};--thumb-surface:${attr(theme.palette.surface)};--thumb-accent:${attr(theme.palette.accent)}">${thumbMedia}</span>
          <span class="theme-copy"><strong>${text(theme.name)}</strong><small>${modeName(theme.mode)}${isDefault ? "<em>默认</em>" : ""}</small></span>
        </button>
        <span class="theme-actions">
          <button class="theme-action default${isDefault ? " current" : ""}" type="button" data-action="default" data-id="${attr(theme.id)}" title="${isDefault ? "当前默认主题" : "设为默认主题"}" ${isDefault ? "disabled" : ""}><i data-lucide="star"></i><span>${isDefault ? "当前默认" : "设为默认"}</span></button>
          <button class="theme-action" type="button" data-action="duplicate" data-id="${attr(theme.id)}" title="创建独立副本"><i data-lucide="copy-plus"></i><span>创建副本</span></button>
          ${theme.builtIn ? "" : `<button class="theme-action delete" type="button" data-action="delete" data-id="${attr(theme.id)}" title="删除这个自定义主题"><i data-lucide="trash-2"></i><span>删除</span></button>`}
        </span>`;
      list.append(row);
    });
    wrapThemeSections(list);
    initializeVideoCovers(list);
    refreshIcons();
  }

  function wrapThemeSections(list) {
    const rows = [...list.children].filter(child => child.classList.contains("theme-item"));
    if (!rows.length) return;
    const definitions = [
      { id: "custom-image", title: "自定义 / 图片", hint: "可编辑的图片主题", empty: "暂无自定义图片", icon: "image" },
      { id: "custom-video", title: "自定义 / 视频", hint: "可编辑的视频主题", empty: "暂无自定义视频", icon: "film" },
      { id: "default", title: "默认主题", hint: "内置主题，不会被覆盖", empty: "没有默认主题", icon: "library" }
    ];
    list.replaceChildren();
    definitions.forEach(definition => {
      const items = rows.filter(row => row.dataset.section === definition.id);
      if (!items.length && state.activeFilter !== "all") return;
      const collapsed = state.collapsedSections.has(definition.id);
      const section = document.createElement("section");
      section.className = `theme-section${collapsed ? " collapsed" : ""}`;
      section.dataset.section = definition.id;
      section.innerHTML = `
        <button class="theme-section-toggle" type="button" data-section-toggle="${definition.id}" aria-expanded="${String(!collapsed)}">
          <span class="theme-section-icon"><i data-lucide="${definition.icon}"></i></span>
          <span class="theme-section-copy"><strong>${definition.title}</strong><small>${definition.hint}</small></span>
          <span class="theme-section-count">${items.length}</span>
          <i class="theme-section-chevron" data-lucide="chevron-down"></i>
        </button>
        <div class="theme-section-items"></div>`;
      const body = $(".theme-section-items", section);
      if (items.length) items.forEach(item => body.append(item));
      else body.innerHTML = `<div class="theme-section-empty">${definition.empty}</div>`;
      list.append(section);
    });
  }

  function toggleThemeSection(id) {
    const section = $$(".theme-section").find(item => item.dataset.section === id);
    if (!section) return;
    const collapsed = section.classList.toggle("collapsed");
    if (collapsed) state.collapsedSections.add(id);
    else state.collapsedSections.delete(id);
    $("[data-section-toggle]", section)?.setAttribute("aria-expanded", String(!collapsed));
  }

  function activateThemeFilter(filter) {
    state.activeFilter = filter;
    $$(".filter-button").forEach(button => button.classList.toggle("active", button.dataset.filter === filter));
    renderThemes();
  }

  function showCustomTheme(id) {
    $("#theme-search").value = "";
    activateThemeFilter("custom");
    selectTheme(id);
    scrollThemeIntoView(id);
  }

  function scrollThemeIntoView(id) {
    window.requestAnimationFrame(() => {
      const button = $$(".theme-select").find(item => item.dataset.select === id);
      button?.closest(".theme-item")?.scrollIntoView({ block: "nearest" });
    });
  }

  async function onThemeListClick(event) {
    const sectionToggle = event.target.closest("[data-section-toggle]");
    if (sectionToggle) {
      toggleThemeSection(sectionToggle.dataset.sectionToggle);
      return;
    }
    const select = event.target.closest("[data-select]");
    if (select) {
      selectTheme(select.dataset.select);
      return;
    }
    const action = event.target.closest("[data-action]");
    if (!action) return;
    const id = action.dataset.id;
    if (action.dataset.action === "default") await setDefaultTheme(id);
    if (action.dataset.action === "duplicate") await duplicateTheme(id, action);
    if (action.dataset.action === "delete") openDeleteDialog(id);
  }

  function selectTheme(id) {
    const item = state.themes.find(entry => entry.theme.id === id);
    if (!item) return;
    state.selectedId = id;
    state.original = clone(item.theme);
    state.draft = clone(item.theme);
    ensureThemeShape(state.draft);
    state.draftMediaUrl = item.mediaUrl || null;
    state.draftBadgeUrl = item.badgeUrl || null;
    state.dirty = false;
    $("#theme-name").value = state.draft.name;
    renderQuickMedia();
    updateThemeSelection();
    renderMode();
    renderInspector();
    applyPreview();
    renderDirtyState();
  }

  function updateThemeSelection() {
    $$(".theme-item").forEach(row => {
      const selected = $("[data-select]", row)?.dataset.select === state.selectedId;
      row.classList.toggle("active", selected);
    });
  }

  function renderQuickMedia() {
    const preview = $("#quick-media-preview");
    const hint = $("#quick-media-hint");
    const button = $("#quick-media-button");
    if (!preview || !hint || !button) return;
    const kind = mediaKind(state.draft?.media?.kind);
    const url = state.draftMediaUrl;
    if (url && kind === "image") {
      preview.innerHTML = `<img src="${attr(url)}" alt="">`;
      hint.textContent = "替换当前图片";
    } else if (url && kind === "video") {
      preview.innerHTML = `<video class="video-cover" src="${attr(url)}" muted playsinline preload="metadata" aria-hidden="true"></video><span class="video-cover-mark"><i data-lucide="film"></i></span>`;
      hint.textContent = "替换当前视频";
      initializeVideoCovers(preview);
    } else {
      preview.innerHTML = `<i data-lucide="image-plus"></i>`;
      hint.textContent = "添加图片或视频";
    }
    button.title = state.draft?.name ? `替换“${state.draft.name}”的背景媒体` : "替换当前主题的背景媒体";
    refreshIcons();
  }

  function ensureThemeShape(theme) {
    theme.mode = String(theme.mode || "standard").toLowerCase();
    theme.media ||= { kind: "none", assetPath: null, opacity: .7, blur: 0, fit: "cover", position: "center", muted: true };
    theme.media.kind = mediaKind(theme.media.kind);
    theme.surfaces ||= { opacity: .88, blur: 14, radius: 6, sidebarOpacity: .9, composerOpacity: .94, bubbleOpacity: .92 };
    theme.layers ||= { media: true, surfaces: true, components: true, badge: true, hero: true, suggestions: true, homeLayout: true };
    theme.badge = { assetPath: "assets/built-in/theme-studio-emblem.png", text: "X", position: "top-left", style: "icon", size: 24, offsetX: 8, offsetY: 6, radius: 6, opacity: .95, backgroundOpacity: .82, borderOpacity: .35, ...(theme.badge || {}) };
  }

  function renderMode() {
    if (!state.draft) return;
    const current = String(state.draft.mode).toLowerCase();
    $$(".mode-switch button").forEach(button => {
      const active = button.dataset.mode === current;
      button.classList.toggle("active", active);
      button.setAttribute("aria-checked", String(active));
    });
    $("#preview-mode-label").textContent = current === "deep" ? "深度模式" : "标准模式";
    $("#codex-preview").classList.toggle("deep-mode", current === "deep");
  }

  function setPreviewView(view) {
    state.activePreview = view;
    $$(".preview-tab").forEach(button => {
      const active = button.dataset.view === view;
      button.classList.toggle("active", active);
      button.setAttribute("aria-selected", String(active));
    });
    $$(".preview-view").forEach(panel => panel.classList.toggle("active", panel.dataset.previewView === view));
    $("#mock-main-title").textContent = view === "home" ? "主页" : view === "task" ? "主题工作台重写" : "设置";
  }

  function setInspectorTab(tab) {
    state.activeInspector = tab;
    $$(".inspector-tab").forEach(button => {
      const active = button.dataset.tab === tab;
      button.classList.toggle("active", active);
      button.setAttribute("aria-selected", String(active));
    });
    renderInspector();
  }

  function renderInspector() {
    if (!state.draft) return;
    const root = $("#inspector-content");
    if (state.activeInspector === "appearance") root.innerHTML = appearanceTemplate();
    if (state.activeInspector === "components") root.innerHTML = componentsTemplate();
    if (state.activeInspector === "badge") root.innerHTML = badgeTemplate();
    if (state.activeInspector === "background") root.innerHTML = backgroundTemplate();
    bindInspectorControls(root);
    initializeVideoCovers(root);
    refreshIcons();
  }

  function appearanceTemplate() {
    const colors = [
      ["canvas", "页面底色", "02", "页面最底层颜色"],
      ["surface", "内容表面", "02", "主内容与侧栏表面"],
      ["elevated", "抬升表面", "04", "卡片、气泡与输入框"],
      ["text", "主要文字", "03", "标题和正文"],
      ["mutedText", "次要文字", "03", "说明和辅助信息"],
      ["border", "边框", "05", "输入框和分隔线"],
      ["accent", "强调色", "01", "选中、按钮与状态"],
      ["accentText", "强调色文字", "05", "强调按钮中的图标文字"]
    ];
    return `<section class="config-group"><div class="config-group-title"><span>界面颜色</span><span>点击色块修改</span></div>${colors.map(([key, label, region, hint]) => colorRow(key, label, region, hint)).join("")}</section>`;
  }

  function componentsTemplate() {
    const deep = String(state.draft.mode).toLowerCase() === "deep";
    return `
      <section class="config-group">
        <div class="config-group-title"><span>表面质感</span><span>01 / 02 / 04 / 05</span></div>
        ${sliderRow("surfaces.opacity", "主区遮罩强度", "02", "数值越低，壁纸越清晰", .05, 1, .01, value => `${Math.round(value * 100)}%`)}
        ${sliderRow("surfaces.blur", "浮层柔化", "04", "仅用于输入框、菜单和弹窗", 0, 40, 1, value => `${value}px`)}
        ${sliderRow("surfaces.radius", "圆角", "04", "卡片、气泡和输入框圆角", 0, 20, 1, value => `${value}px`)}
        ${sliderRow("surfaces.sidebarOpacity", "侧栏遮罩强度", "01", "数值越低，左侧壁纸越清晰", .05, 1, .01, value => `${Math.round(value * 100)}%`)}
        ${sliderRow("surfaces.composerOpacity", "输入框遮罩强度", "05", "数值越高，输入区域越厚实", .2, 1, .01, value => `${Math.round(value * 100)}%`)}
        ${sliderRow("surfaces.bubbleOpacity", "气泡遮罩强度", "04", "用户消息与建议卡片表面", .2, 1, .01, value => `${Math.round(value * 100)}%`)}
      </section>
      <section class="config-group">
        <div class="config-group-title"><span>显示内容</span><span>${deep ? "深度层可单独关闭" : "稳定层"}</span></div>
        ${switchRow("layers.surfaces", "表面配色", "02", "页面与浮层颜色")}
        ${switchRow("layers.components", "组件细节", "05", "按钮、输入框和选中状态")}
        ${switchRow("layers.hero", "主页标题", "03", "深度模式首页标题构图", !deep)}
        ${switchRow("layers.suggestions", "建议卡片", "04", "深度模式首页建议区域", !deep)}
        ${switchRow("layers.homeLayout", "首页构图", "02", "深度模式首页空间布局", !deep)}
      </section>
      ${deep ? `<div class="mode-note"><i data-lucide="shield-check"></i><span>更新后不兼容的深度层会临时跳过，其他颜色和背景继续生效。</span></div>` : ""}`;
  }

  function badgeTemplate() {
    const badge = state.draft.badge;
    const preview = state.draftBadgeUrl
      ? `<div class="asset-preview square"><img src="${attr(state.draftBadgeUrl)}" alt=""></div>`
      : `<div class="asset-preview square empty"><i data-lucide="badge"></i></div>`;
    return `
      <section class="config-group">
        <div class="config-group-title"><span>窗口角标</span><span>01</span></div>
        <div class="asset-picker">${preview}<div class="asset-actions"><button class="small-action" type="button" data-pick="badge"><i data-lucide="image-plus"></i><span>选择图片</span></button><button class="small-action" type="button" data-clear="badge"><i data-lucide="x"></i><span>仅用文字</span></button></div></div>
        ${textRow("badge.text", "角标文字", "01", "没有图片时显示，最多 4 个字符", badge.text || "TS", 4)}
        ${selectRow("badge.style", "外观样式", "01", "纯图标不添加额外方框", [["icon","纯图标"],["glass","玻璃底"],["outline","仅描边"]])}
        ${selectRow("badge.position", "显示位置", "01", "角标在窗口中的位置", [["top-left","左上角"],["top-right","右上角"],["bottom-left","左下角"],["bottom-right","右下角"]])}
        ${sliderRow("badge.size", "角标尺寸", "01", "Codex 内角标大小", 16, 160, 1, value => `${value}px`)}
        ${sliderRow("badge.offsetX", "水平边距", "01", "距离左侧或右侧边缘", 0, 160, 1, value => `${value}px`)}
        ${sliderRow("badge.offsetY", "垂直边距", "01", "距离顶部或底部边缘", 0, 160, 1, value => `${value}px`)}
        ${sliderRow("badge.opacity", "整体可见度", "01", "角标图片或文字的可见程度", 0, 1, .01, value => `${Math.round(value * 100)}%`)}
        ${sliderRow("badge.backgroundOpacity", "底色强度", "01", "玻璃底模式的背景浓度", 0, 1, .01, value => `${Math.round(value * 100)}%`)}
        ${sliderRow("badge.borderOpacity", "边框强度", "01", "玻璃底和描边模式使用", 0, 1, .01, value => `${Math.round(value * 100)}%`)}
        ${sliderRow("badge.radius", "角标圆角", "01", "玻璃底和描边模式使用", 0, 32, 1, value => `${value}px`)}
        ${switchRow("layers.badge", "显示角标", "01", "关闭后不在 Codex 内显示")}
      </section>`;
  }

  function backgroundTemplate() {
    const media = state.draft.media;
    const hasImage = state.draftMediaUrl && mediaKind(media.kind) === "image";
    const hasVideo = state.draftMediaUrl && mediaKind(media.kind) === "video";
    const preview = hasImage
      ? `<div class="asset-preview"><img src="${attr(state.draftMediaUrl)}" alt=""></div>`
      : hasVideo
        ? `<div class="asset-preview"><video class="video-cover" src="${attr(state.draftMediaUrl)}" muted playsinline preload="metadata" aria-hidden="true"></video><span class="video-cover-mark"><i data-lucide="film"></i></span></div>`
      : `<div class="asset-preview empty"><i data-lucide="${mediaKind(media.kind) === "video" ? "film" : "image"}"></i></div>`;
    return `
      <section class="config-group">
        <div class="config-group-title"><span>背景媒体</span><span>图片 / 视频双模式</span></div>
        <div class="asset-picker">${preview}<div class="asset-actions"><button class="small-action" type="button" data-pick="media"><i data-lucide="file-up"></i><span>选择文件</span></button><button class="small-action" type="button" data-clear="media"><i data-lucide="x"></i><span>清除</span></button></div></div>
        ${selectRow("media.kind", "背景类型", "02", "根据选择的文件自动切换", [["none","纯色"],["image","图片"],["video","视频"]])}
        ${sliderRow("media.opacity", "背景可见度", "02", "数值越高，画面越明显", 0, 1, .01, value => `${Math.round(value * 100)}%`)}
        ${sliderRow("media.blur", "壁纸模糊", "02", "设为 0 时保持原图清晰", 0, 40, 1, value => `${value}px`)}
        ${selectRow("media.fit", "填充方式", "02", "背景如何适应窗口", [["cover","填满窗口"],["contain","完整显示"],["fill","拉伸填充"]])}
        ${selectRow("media.position", "画面位置", "02", "主体在背景中的位置", [["center","居中"],["left center","靠左"],["right center","靠右"],["center top","靠上"],["center bottom","靠下"]])}
        ${switchRow("layers.media", "显示背景", "02", "关闭后保留纯色主题")}
      </section>`;
  }

  function colorRow(key, label, region, hint) {
    const value = state.draft.palette[key];
    return `<div class="config-row"><div class="config-copy"><div class="config-label-line"><span>${label}</span><b class="region-tag">${region}</b></div><small>${hint}</small></div><div class="color-control"><input type="color" value="${attr(value)}" data-color="${key}" aria-label="${label}"><input class="color-value" value="${attr(value)}" data-color-text="${key}" maxlength="9" aria-label="${label}色值"></div></div>`;
  }

  function sliderRow(path, label, region, hint, min, max, step, formatter) {
    const value = Number(getPath(state.draft, path));
    const progress = ((value - min) / (max - min)) * 100;
    return `<div class="config-row"><div class="config-copy"><div class="config-label-line"><span>${label}</span><b class="region-tag">${region}</b></div><small>${hint}</small></div><div class="slider-control"><input type="range" min="${min}" max="${max}" step="${step}" value="${value}" style="--range-progress:${progress}%" data-range="${path}" data-min="${min}" data-max="${max}" aria-label="${label}"><span class="range-value" data-range-value="${path}">${formatter(value)}</span></div></div>`;
  }

  function switchRow(path, label, region, hint, disabled = false) {
    return `<div class="config-row"><div class="config-copy"><div class="config-label-line"><span>${label}</span><b class="region-tag">${region}</b></div><small>${hint}</small></div><label class="switch-control"><input type="checkbox" data-switch="${path}" ${getPath(state.draft, path) ? "checked" : ""} ${disabled ? "disabled" : ""}><span></span></label></div>`;
  }

  function selectRow(path, label, region, hint, options) {
    const value = String(getPath(state.draft, path));
    return `<div class="config-row"><div class="config-copy"><div class="config-label-line"><span>${label}</span><b class="region-tag">${region}</b></div><small>${hint}</small></div><select class="select-control" data-select-control="${path}" aria-label="${label}">${options.map(([key, title]) => `<option value="${attr(key)}" ${key === value ? "selected" : ""}>${title}</option>`).join("")}</select></div>`;
  }

  function textRow(path, label, region, hint, value, maxLength) {
    return `<div class="config-row"><div class="config-copy"><div class="config-label-line"><span>${label}</span><b class="region-tag">${region}</b></div><small>${hint}</small></div><input class="text-control" data-text-control="${path}" value="${attr(value)}" maxlength="${maxLength}" aria-label="${label}"></div>`;
  }

  function bindInspectorControls(root) {
    $$('[data-color]', root).forEach(input => input.addEventListener("input", () => {
      state.draft.palette[input.dataset.color] = input.value.toUpperCase();
      const textInput = $(`[data-color-text="${input.dataset.color}"]`, root);
      if (textInput) textInput.value = input.value.toUpperCase();
      markDirty(); applyPreview();
    }));
    $$('[data-color-text]', root).forEach(input => input.addEventListener("change", () => {
      const value = normalizeColor(input.value);
      if (!value) { input.value = state.draft.palette[input.dataset.colorText]; return; }
      state.draft.palette[input.dataset.colorText] = value;
      const picker = $(`[data-color="${input.dataset.colorText}"]`, root);
      if (picker) picker.value = value;
      input.value = value;
      markDirty(); applyPreview();
    }));
    $$('[data-range]', root).forEach(input => input.addEventListener("input", () => {
      const value = Number(input.value);
      setPath(state.draft, input.dataset.range, value);
      const min = Number(input.dataset.min), max = Number(input.dataset.max);
      input.style.setProperty("--range-progress", `${((value - min) / (max - min)) * 100}%`);
      const output = $(`[data-range-value="${input.dataset.range}"]`, root);
      if (output) output.textContent = formatRange(input.dataset.range, value);
      markDirty(); applyPreview();
    }));
    $$('[data-switch]', root).forEach(input => input.addEventListener("change", () => {
      setPath(state.draft, input.dataset.switch, input.checked);
      markDirty(); applyPreview();
    }));
    $$('[data-select-control]', root).forEach(input => input.addEventListener("change", () => {
      setPath(state.draft, input.dataset.selectControl, input.value);
      markDirty(); applyPreview();
    }));
    $$('[data-text-control]', root).forEach(input => input.addEventListener("input", () => {
      setPath(state.draft, input.dataset.textControl, input.value);
      markDirty(); applyPreview();
    }));
    $$('[data-pick]', root).forEach(button => button.addEventListener("click", () => chooseAsset(button.dataset.pick)));
    $$('[data-clear]', root).forEach(button => button.addEventListener("click", () => clearAsset(button.dataset.clear)));
    $$(".config-row", root).forEach(row => {
      row.addEventListener("mouseenter", () => pulseRegion($(".region-tag", row)?.textContent, true));
      row.addEventListener("mouseleave", () => pulseRegion($(".region-tag", row)?.textContent, false));
    });
  }

  function applyPreview() {
    if (!state.draft) return;
    const preview = $("#codex-preview");
    const p = state.draft.palette;
    const s = state.draft.surfaces;
    const m = state.draft.media;
    const variables = {
      "--preview-canvas": p.canvas, "--preview-surface": p.surface, "--preview-elevated": p.elevated,
      "--preview-text": p.text, "--preview-muted": p.mutedText, "--preview-border": p.border,
      "--preview-accent": p.accent, "--preview-accent-text": p.accentText,
      "--preview-opacity": s.opacity, "--sidebar-opacity": s.sidebarOpacity, "--composer-opacity": s.composerOpacity,
      "--bubble-opacity": s.bubbleOpacity, "--preview-blur": `${s.blur}px`, "--preview-radius": `${s.radius}px`,
      "--media-opacity": m.opacity, "--media-blur": `${m.blur}px`, "--preview-fit": m.fit, "--preview-position": m.position
    };
    Object.entries(variables).forEach(([key, value]) => preview.style.setProperty(key, value));
    preview.style.setProperty("--preview-image", state.draftMediaUrl && mediaKind(m.kind) === "image" ? `url("${cssUrl(state.draftMediaUrl)}")` : "none");
    const video = $("#preview-video");
    const videoMode = mediaKind(m.kind) === "video" && state.draftMediaUrl;
    preview.classList.toggle("video-mode", Boolean(videoMode));
    if (videoMode && video.src !== state.draftMediaUrl) { video.src = state.draftMediaUrl; video.play().catch(() => {}); }
    if (!videoMode) { video.pause(); video.removeAttribute("src"); }
    applyStudioBackdrop(p, s, m);

    const badgeImage = $("#mock-badge-image");
    const badgeMark = $("#mock-badge-mark");
    if (state.draftBadgeUrl) {
      badgeImage.src = state.draftBadgeUrl;
      badgeMark.classList.remove("text-only");
    } else {
      badgeImage.removeAttribute("src");
      badgeMark.classList.add("text-only");
    }
    $("#mock-badge-text").textContent = (state.draft.badge.text || "TS").slice(0, 4);
    $("#mock-badge-title").textContent = state.draft.name.toUpperCase();
    const badge = state.draft.badge;
    badgeMark.dataset.style = badge.style;
    badgeMark.style.opacity = badge.opacity;
    badgeMark.style.width = `${Math.round(Math.min(42, Math.max(20, badge.size * .85)))}px`;
    badgeMark.style.setProperty("--badge-background-opacity", badge.backgroundOpacity);
    badgeMark.style.setProperty("--badge-border-opacity", badge.borderOpacity);
    badgeMark.style.setProperty("--badge-radius", `${Math.min(16, badge.radius)}px`);
    preview.classList.toggle("layer-badge-off", !state.draft.layers.badge);
    preview.classList.toggle("layer-hero-off", !state.draft.layers.hero);
    preview.classList.toggle("layer-suggestions-off", !state.draft.layers.suggestions);
    $("#preview-title").textContent = state.draft.name;
    $("#home-greeting").textContent = greeting();
    renderMode();
  }

  function applyStudioBackdrop(palette, surfaces, media) {
    const shell = $("#app-shell");
    const video = $("#studio-video");
    const kind = mediaKind(media.kind);
    const mediaEnabled = state.draft.layers.media !== false && Boolean(state.draftMediaUrl);
    const imageMode = mediaEnabled && kind === "image";
    const videoMode = mediaEnabled && kind === "video";
    const variables = {
      "--studio-canvas": palette.canvas,
      "--studio-surface": palette.surface,
      "--studio-border": palette.border,
      "--studio-image": imageMode ? `url("${cssUrl(state.draftMediaUrl)}")` : "none",
      "--studio-media-opacity": media.opacity,
      "--studio-media-blur": `${media.blur}px`,
      "--studio-fit": media.fit,
      "--studio-position": media.position,
      "--studio-panel-opacity": `${Math.round((.2 + Math.min(1, surfaces.opacity) * .22) * 100)}%`,
      "--studio-sidebar-opacity": `${Math.round((.24 + Math.min(1, surfaces.sidebarOpacity) * .22) * 100)}%`,
      "--studio-surface-blur": `${Math.max(10, surfaces.blur + 6)}px`
    };
    Object.entries(variables).forEach(([key, value]) => shell.style.setProperty(key, value));
    shell.classList.toggle("studio-media-active", mediaEnabled);
    shell.classList.toggle("studio-video-mode", videoMode);

    if (videoMode && video.getAttribute("src") !== state.draftMediaUrl) {
      video.src = state.draftMediaUrl;
      video.muted = true;
      video.play().catch(() => {});
    }
    if (!videoMode && video.hasAttribute("src")) {
      video.pause();
      video.removeAttribute("src");
      video.load();
    }
  }

  function markDirty() {
    state.dirty = JSON.stringify(state.draft) !== JSON.stringify(state.original);
    renderDirtyState();
  }

  function renderDirtyState() {
    if (!state.draft) return;
    const indicator = $("#draft-indicator");
    indicator.classList.toggle("dirty", state.dirty);
    $("span", indicator).textContent = state.dirty ? "有未保存修改" : "已同步";
    setIcon(indicator, state.dirty ? "circle-dot" : "circle-check");
    $("#preview-save-state").textContent = state.dirty ? "当前修改尚未保存" : "当前主题未修改";

    const builtIn = Boolean(state.draft.builtIn);
    const saveCopyButton = $("#save-copy-button");
    const updateButton = $("#update-button");
    const saveRule = $("#theme-save-rule");

    if (!saveCopyButton.classList.contains("is-loading") && !saveCopyButton.classList.contains("is-success")) {
      $("span", saveCopyButton).textContent = builtIn && state.dirty ? "另存修改" : "另存为";
      setIcon(saveCopyButton, "copy-plus");
    }
    saveCopyButton.classList.toggle("recommended", builtIn && state.dirty && !state.busy);
    saveCopyButton.disabled = state.busy;
    saveCopyButton.title = builtIn
      ? "内置主题不可覆盖，请另存为新的自定义主题"
      : "保留当前主题并创建一个独立副本";

    updateButton.disabled = builtIn || !state.dirty || state.busy;
    updateButton.classList.toggle("recommended", !builtIn && state.dirty && !state.busy);
    updateButton.title = builtIn
      ? "内置主题不可覆盖，请使用“另存修改”"
      : state.dirty ? "覆盖保存当前自定义主题" : "当前主题没有需要保存的修改";

    saveRule.classList.toggle("custom", !builtIn);
    saveRule.classList.toggle("dirty", state.dirty);
    $("span", saveRule).textContent = builtIn
      ? state.dirty ? "内置主题不可覆盖，请使用“另存修改”" : "内置主题只读，修改后可另存为新主题"
      : state.dirty ? "我的主题已有修改，可以直接“保存修改”" : "我的主题可以继续编辑并覆盖保存";
    setIcon(saveRule, builtIn ? "lock-keyhole" : "folder-pen");
  }

  function updateDraft(path, value) {
    if (!state.draft) return;
    setPath(state.draft, path, value);
    markDirty();
    if (path === "media.kind") renderThemes();
    if (path.startsWith("media.")) renderQuickMedia();
    applyPreview();
  }

  function resetDraft() {
    if (!state.original) return;
    const item = state.themes.find(entry => entry.theme.id === state.selectedId);
    state.draft = clone(state.original);
    ensureThemeShape(state.draft);
    state.draftMediaUrl = item?.mediaUrl || null;
    state.draftBadgeUrl = item?.badgeUrl || null;
    state.dirty = false;
    $("#theme-name").value = state.draft.name;
    renderQuickMedia();
    renderMode(); renderInspector(); renderThemes(); applyPreview(); renderDirtyState();
    toast("已撤销修改", "恢复到上次保存的主题。", "info");
  }

  async function chooseAsset(kind) {
    if (!state.draft) return;
    setInspectorTab(kind === "badge" ? "badge" : "background");
    try {
      const result = await api(kind === "badge" ? "pickBadge" : "pickMedia", { themeId: state.draft.id });
      if (result.cancelled) return;
      if (kind === "badge") {
        state.draft.badge.assetPath = result.assetPath;
        state.draftBadgeUrl = result.url;
      } else {
        state.draft.media.assetPath = result.assetPath;
        state.draft.media.kind = /\.(mp4|webm|mov)$/i.test(result.assetPath) ? "video" : "image";
        state.draftMediaUrl = result.url;
      }
      markDirty();
      if (kind === "media") renderThemes();
      if (kind === "media") renderQuickMedia();
      renderInspector(); applyPreview();
      const assetName = kind === "badge" ? "角标" : "背景";
      const saveHint = state.draft.builtIn
        ? `预览已更新，请使用“另存修改”保存新的${assetName}。`
        : "预览已更新，点击“保存修改”即可覆盖当前主题。";
      toast(`${assetName}已更换`, saveHint, "success");
    } catch (error) {
      toast("文件未载入", error.message, "error");
    }
  }

  function clearAsset(kind) {
    if (!state.draft) return;
    if (kind === "badge") {
      state.draft.badge.assetPath = null;
      state.draftBadgeUrl = null;
    } else {
      state.draft.media.assetPath = null;
      state.draft.media.kind = "none";
      state.draftMediaUrl = null;
    }
    markDirty();
    if (kind === "media") renderThemes();
    if (kind === "media") renderQuickMedia();
    renderInspector(); applyPreview();
  }

  function openSaveDialog() {
    openThemeCreationDialog("copy");
  }

  function openNewThemeDialog() {
    openThemeCreationDialog("new");
  }

  function openThemeCreationDialog(intent) {
    if (!state.draft) return;
    state.saveIntent = intent;
    const builtIn = Boolean(state.draft.builtIn);
    const draftNameChanged = state.draft.name.trim() !== state.original?.name?.trim();
    $("#save-dialog-title").textContent = intent === "new"
      ? "新建自定义主题"
      : state.dirty ? "另存当前修改" : "另存为新主题";
    $("#save-dialog-subtitle").textContent = intent === "new"
      ? `从“${state.draft.name}”开始，创建后可独立编辑`
      : builtIn
        ? "内置主题保持不变，新主题可继续编辑"
        : "当前主题保持不变，并创建独立副本";
    $("#copy-name").value = intent === "new"
      ? `${state.draft.name} 自定义`
      : builtIn && draftNameChanged ? state.draft.name : `${state.draft.name}${builtIn ? " 自定义" : " 副本"}`;
    $("#save-dialog").showModal();
    $("#copy-name").select();
  }

  async function saveCopy(event) {
    event.preventDefault();
    const name = $("#copy-name").value.trim();
    if (!name || !state.draft) return;
    const intent = state.saveIntent === "new" ? "new" : "copy";
    let saved = false;
    setBusy(true, intent === "new" ? "正在新建主题" : "正在创建主题", false, $("#save-submit-button"));
    try {
      const item = await api("createThemeCopy", { theme: state.draft, name });
      state.themes.push(item);
      showCustomTheme(item.theme.id);
      $("#save-dialog").close();
      saved = true;
      toast(intent === "new" ? "自定义主题已创建" : "主题已保存", `“${item.theme.name}”已加入“我的”主题。`, "success");
    } catch (error) {
      toast("主题未保存", `${error.message} 名称和当前修改已保留，可以直接重试。`, "error");
    } finally {
      setBusy(false);
    }
    if (saved) showButtonSuccess($(intent === "new" ? "#new-theme-button" : "#save-copy-button"), "已创建");
  }

  async function updateCurrentTheme() {
    if (!state.draft || state.draft.builtIn || !state.dirty) return;
    let saved = false;
    setBusy(true, "正在保存修改", false, $("#update-button"));
    try {
      const item = await api("saveTheme", { theme: state.draft });
      replaceThemeItem(item);
      showCustomTheme(item.theme.id);
      saved = true;
      toast("主题已更新", "当前名称、背景和配置修改已经保存。", "success");
    } catch (error) {
      toast("主题未更新", error.message, "error");
    } finally {
      setBusy(false);
    }
    if (saved) showButtonSuccess($("#update-button"), "已保存");
  }

  async function duplicateTheme(id, actionButton = null) {
    const source = state.themes.find(item => item.theme.id === id);
    if (!source) return;
    setBusy(true, "正在复制主题", false, actionButton);
    try {
      const item = await api("duplicateTheme", { id, name: `${source.theme.name} 副本` });
      state.themes.push(item);
      showCustomTheme(item.theme.id);
      toast("副本已创建", "新主题已选中，现在可以修改、保存或删除。", "success");
    } catch (error) {
      toast("副本未创建", error.message, "error");
    } finally { setBusy(false); }
  }

  function openDeleteDialog(id) {
    const item = state.themes.find(entry => entry.theme.id === id);
    if (!item || item.theme.builtIn) return;
    state.deleteId = id;
    $("#delete-theme-name").textContent = item.theme.name;
    $("#delete-dialog").showModal();
  }

  async function deleteTheme(event) {
    event.preventDefault();
    $("#delete-dialog").close();
    if (!state.deleteId) return;
    const id = state.deleteId;
    state.deleteId = null;
    setBusy(true, "正在删除主题");
    try {
      await api("deleteTheme", { id });
      state.themes = state.themes.filter(item => item.theme.id !== id);
      if (state.settings.defaultThemeId === id) state.settings.defaultThemeId = "rain-archive";
      const deletedSelectedTheme = state.selectedId === id;
      renderThemes();
      if (deletedSelectedTheme) {
        const nextVisibleId = $(".theme-select")?.dataset.select;
        if (nextVisibleId) {
          selectTheme(nextVisibleId);
        } else {
          $("#theme-search").value = "";
          activateThemeFilter("all");
          const fallback = state.themes.find(item => item.theme.id === state.settings.defaultThemeId) || state.themes[0];
          if (fallback) selectTheme(fallback.theme.id);
        }
      }
      toast("主题已删除", "主题库已经更新。", "success");
    } catch (error) {
      toast("主题未删除", error.message, "error");
    } finally { setBusy(false); }
  }

  async function setDefaultTheme(id) {
    try {
      state.settings = await api("setDefaultTheme", { id });
      renderThemes();
      const item = state.themes.find(entry => entry.theme.id === id);
      toast("默认主题已更换", item ? `下次自动使用“${item.theme.name}”。` : "设置已保存。", "success");
    } catch (error) { toast("默认主题未更换", error.message, "error"); }
  }

  async function applyCurrentTheme(label, actionButton = null) {
    if (!state.draft || state.busy) return;
    setBusy(true, label, false, actionButton);
    try {
      const result = await api("applyTheme", { theme: state.draft });
      if (result.success) {
        toast("皮肤已应用", result.message, "success");
        renderCompatibility(result.suspendedLayers || []);
      } else if (result.requiresRestart) {
        $("#reconnect-dialog").showModal();
      } else {
        toast("暂未应用", result.message, "info");
        renderCompatibility(result.suspendedLayers || [], true);
      }
    } catch (error) {
      toast("皮肤未应用", error.message, "error");
      renderCompatibility([], true);
    } finally { setBusy(false); }
  }

  async function restartAndApply(event) {
    event.preventDefault();
    $("#reconnect-dialog").close();
    if (!state.draft || state.busy) return;
    setBusy(true, "正在重新连接 Codex", false);
    try {
      const result = await api("restartAndApply", { theme: state.draft });
      if (result.success) {
        toast("皮肤已应用", result.message, "success");
        renderCompatibility(result.suspendedLayers || []);
      } else {
        toast("暂未应用", result.message, "info");
        renderCompatibility(result.suspendedLayers || [], true);
      }
    } catch (error) {
      toast("重新连接未完成", error.message, "error");
      renderCompatibility([], true);
    } finally { setBusy(false); }
  }

  async function refreshWorkbench() {
    if (state.busy) return;
    $("#refresh-button").classList.add("spinning");
    try {
      const selected = state.selectedId;
      const data = await api("refresh");
      state.themes = data.themes || [];
      state.settings = data.settings || {};
      state.status = data.status || {};
      $("#auto-apply-toggle").checked = Boolean(state.settings.brokerEnabled);
      renderStatus(); renderThemes();
      selectTheme(state.themes.some(item => item.theme.id === selected) ? selected : state.themes[0]?.theme.id);
      toast("已刷新", "主题图库和 Codex 状态已更新。", "success");
    } catch (error) { toast("刷新未完成", error.message, "error"); }
    finally { window.setTimeout(() => $("#refresh-button").classList.remove("spinning"), 260); }
  }

  function renderStatus() {
    const status = state.status || {};
    const statusName = String(status.state ?? "idle").toLowerCase();
    const dot = $("#status-dot");
    dot.className = "status-dot";
    if (/launch|wait|apply|locat/.test(statusName)) dot.classList.add("busy");
    else if (/fault|notfound/.test(statusName)) dot.classList.add("error");
    else if (/native/.test(statusName)) dot.classList.add("warning");
    else dot.classList.add("ready");
    $("#status-text").textContent = status.message || "Codex 已就绪";
    $("#codex-version").textContent = status.codexVersion ? `Codex ${status.codexVersion}` : "";
  }

  function renderCompatibility(suspended, warning = false) {
    const line = $("#compatibility-line");
    line.classList.toggle("warning", warning || suspended.length > 0);
    $("span", line).textContent = suspended.length ? `已跳过不兼容层：${suspended.join("、")}` : warning ? "Codex 保持原生界面" : "当前配置可用";
    const icon = $("svg", line);
    if (icon) icon.setAttribute("data-lucide", warning || suspended.length ? "triangle-alert" : "circle-check");
    refreshIcons();
  }

  function setBusy(busy, label = "正在处理", showOverlay = true, actionButton = null) {
    if (!busy && state.busyButton) {
      setButtonLoading(state.busyButton, false);
      state.busyButton = null;
    }
    state.busy = busy;
    if (busy && actionButton) {
      state.busyButton = actionButton;
      setButtonLoading(actionButton, true, label);
    }
    $("#busy-overlay").hidden = !busy || !showOverlay;
    $("#busy-label").textContent = label;
    ["#refresh-button", "#save-copy-button", "#new-theme-button", "#launch-button", "#apply-button", "#wide-apply-button"].forEach(selector => $(selector).disabled = busy);
    $("#status-text").textContent = busy ? label : (state.status.message || "Codex 已就绪");
    renderDirtyState();
  }

  function setButtonLoading(button, loading, label = "正在处理") {
    if (!button) return;
    const labelNode = $("span", button);
    const icon = $("[data-lucide]", button);
    if (loading) {
      button.dataset.idleLabel = labelNode?.textContent || "";
      button.dataset.idleIcon = icon?.getAttribute("data-lucide") || "circle";
      button.dataset.idleDisabled = String(button.disabled);
      if (labelNode) labelNode.textContent = label;
      if (icon) icon.setAttribute("data-lucide", "loader-circle");
      button.classList.add("is-loading", "spinning");
      button.disabled = true;
    } else {
      if (labelNode) labelNode.textContent = button.dataset.idleLabel || labelNode.textContent;
      if (icon) icon.setAttribute("data-lucide", button.dataset.idleIcon || "circle");
      button.classList.remove("is-loading", "spinning");
      button.disabled = button.dataset.idleDisabled === "true";
      delete button.dataset.idleLabel;
      delete button.dataset.idleIcon;
      delete button.dataset.idleDisabled;
    }
    refreshIcons();
  }

  function showButtonSuccess(button, label) {
    if (!button) return;
    const labelNode = $("span", button);
    const icon = $("[data-lucide]", button);
    const previousLabel = labelNode?.textContent || "";
    const previousIcon = icon?.getAttribute("data-lucide") || "circle-check";
    if (labelNode) labelNode.textContent = label;
    if (icon) icon.setAttribute("data-lucide", "check");
    button.classList.add("is-success");
    button.disabled = true;
    refreshIcons();
    window.setTimeout(() => {
      if (labelNode) labelNode.textContent = previousLabel;
      const currentIcon = $("[data-lucide]", button);
      if (currentIcon) currentIcon.setAttribute("data-lucide", previousIcon);
      button.classList.remove("is-success");
      renderDirtyState();
      refreshIcons();
    }, 920);
  }

  function toast(title, message, type = "success") {
    const element = document.createElement("div");
    element.className = `toast ${type}`;
    const icon = type === "error" ? "triangle-alert" : type === "info" ? "info" : "circle-check";
    element.innerHTML = `<span><i data-lucide="${icon}"></i></span><div><strong>${text(title)}</strong><small>${text(message || "")}</small></div>`;
    $("#toast-region").append(element);
    refreshIcons();
    window.setTimeout(() => element.remove(), 4000);
  }

  function pulseRegion(region, active, timed = false) {
    if (!region) return;
    $$(`.mapped-region[data-region="${region}"]`).forEach(element => element.classList.toggle("region-pulse", active));
    if (timed) window.setTimeout(() => pulseRegion(region, false), 720);
  }

  function createRipple(event) {
    const button = event.target.closest("button, .auto-apply");
    if (!button || button.disabled) return;
    const rect = button.getBoundingClientRect();
    const ripple = document.createElement("span");
    ripple.className = "ripple";
    ripple.style.left = `${event.clientX - rect.left}px`;
    ripple.style.top = `${event.clientY - rect.top}px`;
    button.append(ripple);
    window.setTimeout(() => ripple.remove(), 450);
  }

  function replaceThemeItem(item) {
    const index = state.themes.findIndex(entry => entry.theme.id === item.theme.id);
    if (index >= 0) state.themes[index] = item;
    else state.themes.push(item);
  }

  function setIcon(container, name) {
    const icon = $("[data-lucide]", container);
    if (!icon || icon.getAttribute("data-lucide") === name) return;
    icon.setAttribute("data-lucide", name);
    refreshIcons();
  }

  function getPath(object, path) { return path.split(".").reduce((value, key) => value?.[key], object); }
  function setPath(object, path, value) {
    const keys = path.split(".");
    const final = keys.pop();
    const target = keys.reduce((current, key) => current[key], object);
    target[final] = value;
  }
  function initializeVideoCovers(root = document) {
    $$("video.video-cover", root).forEach(video => {
      const showCoverFrame = () => {
        const duration = Number.isFinite(video.duration) ? video.duration : 0;
        const coverTime = duration > .08 ? Math.min(.08, duration / 2) : 0;
        try { video.currentTime = coverTime; } catch { /* Metadata may still be settling. */ }
      };
      video.addEventListener("seeked", () => video.pause(), { once: true });
      if (video.readyState >= 1) showCoverFrame();
      else video.addEventListener("loadedmetadata", showCoverFrame, { once: true });
    });
  }
  function mediaKind(value) { return String(value || "none").toLowerCase(); }
  function modeName(value) { return String(value).toLowerCase() === "deep" ? "深度模式" : "标准模式"; }
  function normalizeColor(value) { const result = value.trim().toUpperCase(); return /^#[0-9A-F]{6}([0-9A-F]{2})?$/.test(result) ? result : null; }
  function formatRange(path, value) {
    if (path.includes("opacity") || path.includes("Opacity")) return `${Math.round(value * 100)}%`;
    return `${value}px`;
  }
  function colorLuminance(hex) {
    const value = hex.replace("#", "").slice(0, 6);
    const parts = [0, 2, 4].map(index => parseInt(value.slice(index, index + 2), 16) / 255).map(channel => channel <= .03928 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4);
    return .2126 * parts[0] + .7152 * parts[1] + .0722 * parts[2];
  }
  function isWarm(hex) { const value = hex.replace("#", ""); return parseInt(value.slice(0, 2), 16) > parseInt(value.slice(4, 6), 16) * 1.15; }
  function greeting() { const hour = new Date().getHours(); return hour < 6 ? "夜深了" : hour < 11 ? "早上好" : hour < 14 ? "中午好" : hour < 18 ? "下午好" : "晚上好"; }
  function text(value) { const element = document.createElement("span"); element.textContent = String(value ?? ""); return element.innerHTML; }
  function attr(value) { return text(value).replace(/"/g, "&quot;"); }
  function cssUrl(value) { return String(value).replace(/\\/g, "\\\\").replace(/"/g, '\\"').replace(/[\n\r]/g, ""); }
  function refreshIcons() {
    const available = Boolean(window.lucide?.createIcons);
    document.documentElement.classList.toggle("icons-unavailable", !available);
    if (available) window.lucide.createIcons({ attrs: { "stroke-width": 1.7 } });
  }

  document.addEventListener("DOMContentLoaded", initialize);
})();
