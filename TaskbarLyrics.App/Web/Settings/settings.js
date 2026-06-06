const sourceNames = {
  QQMusic: "QQ音乐",
  Netease: "网易云音乐",
  Kugou: "酷狗音乐",
  Spotify: "Spotify"
};

let state = null;
let fonts = [];
let draggedSource = null;

const bridge = {
  post(message) {
    window.chrome?.webview?.postMessage(JSON.stringify(message));
  }
};

function updateSetting(key, value) {
  if (!state) return;
  state[key] = value;
  if (key === "foregroundColor") updateSwatch(value);
  bridge.post({ type: "update", key, value });
}

function setState(nextState, fontList = fonts) {
  state = nextState;
  fonts = fontList;
  renderFonts();
  renderControls();
  renderOrder();
}

function renderFonts() {
  const select = document.querySelector('select[data-key="fontFamily"]');
  if (!select || select.options.length > 0) return;

  for (const font of fonts) {
    const option = document.createElement("option");
    option.value = font;
    option.textContent = font;
    select.appendChild(option);
  }
}

function renderControls() {
  if (!state) return;

  for (const input of document.querySelectorAll("[data-key]")) {
    const key = input.dataset.key;
    if (!(key in state)) continue;

    if (input.type === "checkbox") {
      input.checked = Boolean(state[key]);
    } else {
      input.value = state[key] ?? "";
    }
  }

  updateSwatch(state.foregroundColor);
}

function updateSwatch(color) {
  const swatch = document.getElementById("colorSwatch");
  if (!swatch) return;
  swatch.style.background = normalizeCssColor(color);
  swatch.animate(
    [{ transform: "scale(1)" }, { transform: "scale(1.14)" }, { transform: "scale(1)" }],
    { duration: 160, easing: "ease-out" }
  );
}

function normalizeCssColor(color) {
  if (typeof color !== "string") return "#fff";
  if (/^#[0-9a-fA-F]{8}$/.test(color)) {
    return `#${color.slice(3)}`;
  }
  return color;
}

function renderOrder() {
  const order = document.getElementById("sourceOrder");
  if (!order || !state) return;

  order.innerHTML = "";
  for (const source of state.sourceRecognitionOrder ?? []) {
    const item = document.createElement("div");
    item.className = "order-item";
    item.draggable = true;
    item.dataset.source = source;
    item.innerHTML = `<span class="handle">⋮⋮</span><strong>${sourceNames[source] ?? source}</strong>`;
    item.addEventListener("dragstart", onOrderDragStart);
    item.addEventListener("dragover", onOrderDragOver);
    item.addEventListener("drop", onOrderDrop);
    item.addEventListener("dragend", onOrderDragEnd);
    order.appendChild(item);
  }
}

function onOrderDragStart(event) {
  draggedSource = event.currentTarget.dataset.source;
  event.currentTarget.classList.add("dragging");
  event.dataTransfer.effectAllowed = "move";
}

function onOrderDragOver(event) {
  event.preventDefault();
  event.dataTransfer.dropEffect = "move";
}

function onOrderDrop(event) {
  event.preventDefault();
  const targetSource = event.currentTarget.dataset.source;
  if (!state || !draggedSource || draggedSource === targetSource) return;

  const order = [...state.sourceRecognitionOrder];
  const from = order.indexOf(draggedSource);
  const to = order.indexOf(targetSource);
  if (from < 0 || to < 0) return;

  order.splice(from, 1);
  order.splice(to, 0, draggedSource);
  state.sourceRecognitionOrder = order;
  renderOrder();
  bridge.post({ type: "reorderSources", value: order });
}

function onOrderDragEnd(event) {
  event.currentTarget.classList.remove("dragging");
  draggedSource = null;
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function formatNumber(value, decimals) {
  if (decimals <= 0) return String(Math.round(value));
  return Number(value.toFixed(decimals)).toString();
}

function setupEvents() {
  document.querySelectorAll("input, select").forEach((element) => {
    element.addEventListener("change", () => {
      const key = element.dataset.key;
      if (!key) return;

      const value = element.type === "checkbox"
        ? element.checked
        : parseInputValue(element);
      updateSetting(key, value);
    });
  });

  document.querySelectorAll(".stepper").forEach((stepper) => {
    stepper.addEventListener("click", (event) => {
      const button = event.target.closest("button[data-step]");
      if (!button) return;

      const key = stepper.dataset.key;
      const input = stepper.querySelector("input");
      const min = Number(stepper.dataset.min);
      const max = Number(stepper.dataset.max);
      const step = Number(stepper.dataset.step);
      const decimals = Number(stepper.dataset.decimals);
      const direction = Number(button.dataset.step);
      const current = Number.parseFloat(input.value || state?.[key] || min);
      const next = clamp(current + direction * step, min, max);
      input.value = formatNumber(next, decimals);
      updateSetting(key, Number(input.value));
    });
  });

  document.getElementById("colorPicker")?.addEventListener("click", (event) => {
    if (event.target.matches("input")) return;
    bridge.post({ type: "pickColor" });
  });

  document.getElementById("resetButton")?.addEventListener("click", () => {
    bridge.post({ type: "resetDefaults" });
  });

  document.getElementById("clearCacheButton")?.addEventListener("click", () => {
    bridge.post({ type: "clearCache" });
  });

  document.getElementById("sidebarToggle")?.addEventListener("click", () => {
    document.querySelector(".window")?.classList.toggle("collapsed");
  });

  document.querySelectorAll(".nav-item").forEach((button) => {
    button.addEventListener("click", () => {
      document.querySelectorAll(".nav-item").forEach((item) => item.classList.remove("active"));
      button.classList.add("active");
      document.getElementById(button.dataset.target)?.scrollIntoView({ behavior: "smooth", block: "start" });
    });
  });

  document.getElementById("content")?.addEventListener("scroll", updateActiveNav);
}

function parseInputValue(element) {
  const key = element.dataset.key;
  if (["fontSize", "backgroundOpacity", "windowWidth", "xOffset", "yOffset"].includes(key)) {
    return Number(element.value);
  }
  return element.value;
}

function updateActiveNav() {
  const sections = [...document.querySelectorAll("section.card")];
  const active = sections
    .map((section) => ({ id: section.id, distance: Math.abs(section.getBoundingClientRect().top - 110) }))
    .sort((a, b) => a.distance - b.distance)[0];
  if (!active) return;

  document.querySelectorAll(".nav-item").forEach((item) => {
    item.classList.toggle("active", item.dataset.target === active.id);
  });
}

window.settingsApp = { setState };

setupEvents();
bridge.post({ type: "ready" });
