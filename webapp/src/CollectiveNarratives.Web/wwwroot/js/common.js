// Gedeeld door alle pagina's: SignalR-verbinding, client-id, code-invulvakjes en teksten.

function getClientId() {
  // Vaste id per telefoon, zodat opnieuw verbinden geen extra kijker of dubbele stem geeft.
  let id = null;
  try {
    id = localStorage.getItem("cn-client-id");
  } catch {}
  if (!id) {
    id = Math.random().toString(36).slice(2) + Date.now().toString(36);
    try {
      localStorage.setItem("cn-client-id", id);
    } catch {}
  }
  return id;
}

// De sessiecode uit het adres, bv. /poll/365895 -> "365895" (of "" als er geen is).
function codeFromPath() {
  const m = location.pathname.match(/\/(\d{6})\/?$/);
  return m ? m[1] : "";
}

// Verbinding met /hubs/session die altijd opnieuw probeert.
// opts.on: { State: fn, SessionEnded: fn, ... }
// opts.onConnected(conn): na elke (her)verbinding, bv. om opnieuw te joinen.
// opts.onStatus(online): voor het bolletje "Verbonden".
// opts.accessToken: alleen voor de game (hostpagina). opts.query: extra querystring, bv. "device=...".
function startHub(opts) {
  const builder = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/session" + (opts.query ? "?" + opts.query : ""), opts.accessToken ? { accessTokenFactory: () => opts.accessToken } : {})
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => 2000 })
    .configureLogging(signalR.LogLevel.Warning);
  const conn = builder.build();
  conn.keepAliveIntervalInMilliseconds = 5000; // de server verbreekt na 15 s stilte
  let stopped = false;

  for (const [name, fn] of Object.entries(opts.on || {})) conn.on(name, fn);
  conn.onreconnecting(() => opts.onStatus && opts.onStatus(false));
  conn.onreconnected(async () => {
    opts.onStatus && opts.onStatus(true);
    if (opts.onConnected) await opts.onConnected(conn);
  });
  conn.onclose(() => {
    opts.onStatus && opts.onStatus(false);
    if (!stopped) setTimeout(start, 2000);
  });

  async function start() {
    if (stopped) return;
    try {
      await conn.start();
    } catch {
      opts.onStatus && opts.onStatus(false);
      setTimeout(start, 2000);
      return;
    }
    opts.onStatus && opts.onStatus(true);
    if (opts.onConnected) await opts.onConnected(conn);
  }
  start();

  return {
    conn,
    invoke: (...args) => conn.invoke(...args),
    send(...args) {
      if (conn.state === signalR.HubConnectionState.Connected) conn.send(...args).catch(() => {});
    },
    stop() {
      stopped = true;
      return conn.stop();
    },
  };
}

// Zes vakjes voor de code. onComplete(code) zodra alle 6 cijfers er staan.
// Eigenlijk één onzichtbaar invoerveld over 6 getekende vakjes: zo werken snel typen, plakken ("365 895")
// en automatisch invullen altijd.
function codeInput(container, onComplete) {
  container.classList.add("code-input");
  container.innerHTML = "";
  const boxes = [];
  for (let i = 0; i < 6; i++) {
    const b = document.createElement("div");
    b.className = "box";
    container.appendChild(b);
    boxes.push(b);
  }
  const input = document.createElement("input");
  input.type = "text";
  input.inputMode = "numeric";
  input.autocomplete = "one-time-code";
  input.setAttribute("aria-label", "6-digit code");
  container.appendChild(input);

  function render() {
    const v = input.value;
    const focused = document.activeElement === input;
    boxes.forEach((b, i) => {
      b.textContent = v[i] || "";
      b.classList.toggle("current", focused && i === Math.min(v.length, 5));
    });
  }

  input.addEventListener("input", () => {
    input.value = input.value.replace(/\D/g, "").slice(0, 6);
    render();
    if (input.value.length === 6) onComplete(input.value);
  });
  input.addEventListener("focus", render);
  input.addEventListener("blur", render);
  // Cursor altijd achteraan, zodat backspace het laatste cijfer wist.
  input.addEventListener("keyup", () => input.setSelectionRange(input.value.length, input.value.length));

  return {
    focus: () => input.focus(),
    clear() {
      input.value = "";
      input.focus();
      render();
    },
    setDisabled(d) {
      input.disabled = d;
      container.classList.toggle("disabled", d);
    },
  };
}

// Na het einde blijven kijken of dezelfde game opnieuw start; dan meteen naar die sessie.
// proof: "screenKey=..." (groot scherm) of "clientId=..." (telefoon). Zonder bewijs geeft de server niets prijs.
// minutes: zo lang blijven proberen.
function followNextSession(code, page, proof, minutes) {
  const until = Date.now() + minutes * 60 * 1000;
  const check = async () => {
    try {
      const res = await fetch(`/api/sessions/${code}/next?${proof}`, { cache: "no-store" });
      if (res.status === 200) return location.replace(`/${page}/${(await res.json()).next}`);
    } catch {}
    if (Date.now() < until) setTimeout(check, 2000);
  };
  check();
}

// Bestaat de code? Geeft "active", "ended", "unknown", "invalid" of "rateLimited".
async function checkCode(code) {
  try {
    const res = await fetch(`/api/sessions/${code}`);
    return (await res.json()).status;
  } catch {
    return "offline";
  }
}

const CODE_ERRORS = {
  unknown: "This code doesn't exist. Take another look.",
  invalid: "A code has 6 digits.",
  ended: "This session has ended.",
  rateLimited: "Too many wrong codes. Wait a moment and try again.",
  notReady: "The game isn't ready yet. Hang on...",
  offline: "No connection to the server.",
};

// 365895 -> "365 895", leesbaarder op een groot scherm.
function prettyCode(code) {
  return code ? code.slice(0, 3) + " " + code.slice(3) : "";
}

const ENDING_NAMES = {
  held: "Hero of the neighbourhood",
  weg: "Out just in time",
  cadeau: "The present is gone",
};
const ENDING_ORDER = ["held", "weg", "cadeau"];

function winnerLabel(result) {
  const o = result.options.find((o) => o.id === result.winner);
  return o ? o.label : "";
}

function setStatus(el, online, onText, offText) {
  el.textContent = online ? onText : offText;
  el.classList.toggle("offline", !online);
}
