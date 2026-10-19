// Stemserver voor Home Alone VR.
// Telefoons (publiek) praten via WebSocket. Unity en de host-pagina gebruiken de HTTP-API.
// De afspraken met Unity staan in UNITY-KOPPELING.md: pas die mee aan als je hier iets verandert.

const http = require("http");
const fs = require("fs");
const os = require("os");
const path = require("path");
const QRCode = require("qrcode");
const { WebSocketServer } = require("ws");
const CHOICES = require("./choices");

const PORT = Number(process.env.PORT) || 3000;
const DEFAULT_SECONDS = 12;
const DEFAULT_TOGETHER_SECONDS = 5;
const ENDINGS = ["held", "weg", "cadeau"];
const PUBLIC_DIR = path.join(__dirname, "public");

// LAN-adressen, wifi eerst en virtuele adapters (VMware, VirtualBox, WSL...) achteraan.
function lanIps() {
  const score = (name, ip) => {
    if (/wi-?fi|wlan|wireless/i.test(name)) return 0;
    if (/vmware|virtualbox|vbox|vethernet|hyper-v|docker|wsl/i.test(name) || ip.startsWith("192.168.56.")) return 2;
    return 1;
  };
  return Object.entries(os.networkInterfaces())
    .flatMap(([name, list]) => list.map((i) => ({ name, ...i })))
    .filter((i) => i.family === "IPv4" && !i.internal)
    .sort((a, b) => score(a.name, a.address) - score(b.name, b.address))
    .map((i) => i.address);
}

const JOIN_URL = (process.env.PUBLIC_URL || `http://${lanIps()[0] || "localhost"}:${PORT}`).replace(/\/?$/, "/");

// ---------- Staat ----------

let current = null; // lopende stemming
let lastResult = null; // laatst afgeronde stemming
const history = []; // alle resultaten van deze ronde
let voteCounter = 0;
let voteTimer = null;

let together = null; // lopende samen-actie
let lastTogether = { attempt: 0, success: false, presses: 0, needed: 0 };
let togetherTimer = null;

let ending = "";

function optionsWithCounts(options, votes) {
  return options.map((o) => ({
    id: o.id,
    label: o.label,
    count: [...votes.values()].filter((v) => v === o.id).length,
  }));
}

function startVote(id, seconds, message) {
  const def = CHOICES[id];
  if (!def) throw new Error(`Onbekende keuze: ${id}`);
  seconds = Number(seconds) > 0 ? Number(seconds) : DEFAULT_SECONDS;

  clearTimeout(voteTimer);
  current = {
    voteId: ++voteCounter,
    id,
    question: def.question,
    message: message || "",
    options: def.options,
    seconds,
    endsAt: Date.now() + seconds * 1000,
    votes: new Map(), // clientId -> optie-id (laatste druk telt)
  };
  voteTimer = setTimeout(endVote, seconds * 1000);
  console.log(`Stemming ${current.voteId} gestart: ${id} (${seconds}s)`);
  broadcastState();
  return publicVote();
}

function endVote() {
  if (!current) return null;
  clearTimeout(voteTimer);
  const options = optionsWithCounts(current.options, current.votes);
  const max = Math.max(...options.map((o) => o.count));
  const top = options.filter((o) => o.count === max);
  // Gelijkspel of niemand stemt: winner leeg, Kevin beslist.
  const winner = max > 0 && top.length === 1 ? top[0].id : "";

  lastResult = {
    voteId: current.voteId,
    id: current.id,
    winner,
    decidedBy: winner ? "publiek" : "kevin",
    total: current.votes.size,
    options,
  };
  history.push(lastResult);
  console.log(`Stemming ${current.voteId} klaar: ${current.id} -> ${winner || "Kevin beslist"}`);
  current = null;
  broadcastState();
  return lastResult;
}

function castVote(clientId, voteId, optionId) {
  if (!current || current.voteId !== voteId) return;
  if (!current.options.some((o) => o.id === optionId)) return;
  current.votes.set(clientId, optionId);
  broadcastState();
}

function decide(voteId, optionId) {
  const result = history.find((r) => r.voteId === Number(voteId));
  if (!result) throw new Error(`Onbekende voteId: ${voteId}`);
  if (!result.options.some((o) => o.id === optionId)) throw new Error(`Onbekende optie: ${optionId}`);
  result.winner = optionId;
  result.decidedBy = "kevin";
  broadcastState();
  return result;
}

function startTogether(label, seconds) {
  seconds = Number(seconds) > 0 ? Number(seconds) : DEFAULT_TOGETHER_SECONDS;
  clearTimeout(togetherTimer);
  together = {
    label: label || "Druk!",
    seconds,
    endsAt: Date.now() + seconds * 1000,
    presses: new Set(),
    needed: Math.max(1, viewerCount()),
  };
  togetherTimer = setTimeout(() => endTogether(false), seconds * 1000);
  console.log(`Samen-actie gestart: "${together.label}" (${together.needed} nodig)`);
  broadcastState();
  return publicTogether();
}

function endTogether(success) {
  if (!together) return;
  clearTimeout(togetherTimer);
  lastTogether = {
    attempt: lastTogether.attempt + 1,
    success,
    presses: together.presses.size,
    needed: together.needed,
  };
  console.log(`Samen-actie ${success ? "gelukt" : "mislukt"} (${lastTogether.presses}/${lastTogether.needed})`);
  together = null;
  broadcastState();
}

function press(clientId) {
  if (!together) return;
  together.presses.add(clientId);
  if (together.presses.size >= together.needed) endTogether(true);
  else broadcastState();
}

function setEnding(id) {
  if (!ENDINGS.includes(id)) throw new Error(`Onbekend einde: ${id}`);
  ending = id;
  broadcastState();
}

function reset() {
  clearTimeout(voteTimer);
  clearTimeout(togetherTimer);
  current = null;
  lastResult = null;
  history.length = 0;
  together = null;
  lastTogether = { attempt: 0, success: false, presses: 0, needed: 0 };
  ending = "";
  console.log("Nieuwe ronde");
  broadcastState();
}

function remaining(endsAt) {
  return Math.max(0, Math.ceil((endsAt - Date.now()) / 1000));
}

function publicVote() {
  if (!current) return null;
  return {
    voteId: current.voteId,
    id: current.id,
    question: current.question,
    message: current.message,
    seconds: current.seconds,
    remaining: remaining(current.endsAt),
    total: current.votes.size,
    options: optionsWithCounts(current.options, current.votes),
  };
}

function publicTogether() {
  if (!together) return { active: false, label: "", seconds: 0, remaining: 0, presses: 0, needed: 0 };
  return {
    active: true,
    label: together.label,
    seconds: together.seconds,
    remaining: remaining(together.endsAt),
    presses: together.presses.size,
    needed: together.needed,
  };
}

function viewerCount() {
  const ids = new Set();
  for (const ws of wss.clients) if (ws.role === "phone" && ws.readyState === ws.OPEN) ids.add(ws.clientId);
  return ids.size;
}

function getState() {
  return {
    viewers: viewerCount(),
    active: !!current,
    vote: publicVote(),
    last: lastResult,
    together: publicTogether(),
    lastTogether,
    ending,
    history,
  };
}

// ---------- HTTP (statische bestanden + API) ----------

const MIME = { ".html": "text/html; charset=utf-8", ".css": "text/css", ".js": "text/javascript" };

function sendJson(res, status, data) {
  res.writeHead(status, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
  res.end(JSON.stringify(data));
}

function readBody(req) {
  return new Promise((resolve) => {
    let body = "";
    req.on("data", (c) => (body += c));
    req.on("end", () => {
      try {
        resolve(body ? JSON.parse(body) : {});
      } catch {
        resolve({});
      }
    });
  });
}

// POST-routes: elk krijgt de JSON-body en geeft het antwoord terug (of gooit een fout -> 400).
const POST_ROUTES = {
  "/api/start": (b) => startVote(b.id, b.seconds, b.message),
  "/api/stop": () => endVote(),
  "/api/decide": (b) => decide(b.voteId, b.option),
  "/api/together": (b) => startTogether(b.label, b.seconds),
  "/api/end": (b) => (setEnding(b.ending), { ok: true }),
  "/api/reset": () => (reset(), { ok: true }),
};

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://localhost");

  if (req.method === "OPTIONS") {
    res.writeHead(204, {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST",
      "Access-Control-Allow-Headers": "Content-Type",
    });
    return res.end();
  }

  if (req.method === "POST" && POST_ROUTES[url.pathname]) {
    const body = await readBody(req);
    try {
      return sendJson(res, 200, POST_ROUTES[url.pathname](body));
    } catch (e) {
      return sendJson(res, 400, { error: e.message });
    }
  }

  if (req.method === "GET") {
    if (url.pathname === "/api/state") return sendJson(res, 200, getState());
    if (url.pathname === "/api/choices") return sendJson(res, 200, CHOICES);
    if (url.pathname === "/api/info") return sendJson(res, 200, { joinUrl: JOIN_URL });
    if (url.pathname === "/api/qr.png") {
      const png = await QRCode.toBuffer(JOIN_URL, { width: 512, margin: 2 });
      res.writeHead(200, { "Content-Type": "image/png", "Access-Control-Allow-Origin": "*" });
      return res.end(png);
    }
  }

  // Statische pagina's
  const file = url.pathname === "/" ? "index.html" : url.pathname === "/host" ? "host.html" : url.pathname.slice(1);
  const filePath = path.join(PUBLIC_DIR, path.normalize(file));
  if (!filePath.startsWith(PUBLIC_DIR)) {
    res.writeHead(403);
    return res.end();
  }
  fs.readFile(filePath, (err, data) => {
    if (err) {
      res.writeHead(404);
      return res.end("Niet gevonden");
    }
    res.writeHead(200, { "Content-Type": MIME[path.extname(filePath)] || "application/octet-stream" });
    res.end(data);
  });
});

// ---------- WebSocket (telefoons + host-pagina) ----------

const wss = new WebSocketServer({ server, path: "/ws" });

function broadcastState() {
  const msg = JSON.stringify({ type: "state", ...getState() });
  for (const ws of wss.clients) if (ws.readyState === ws.OPEN) ws.send(msg);
}

wss.on("connection", (ws) => {
  ws.role = "unknown";

  ws.on("message", (raw) => {
    let msg;
    try {
      msg = JSON.parse(raw);
    } catch {
      return;
    }

    if (msg.type === "hello") {
      // { type:"hello", role:"phone"|"host", clientId }
      ws.role = msg.role;
      ws.clientId = String(msg.clientId || Math.random());
      broadcastState();
    } else if (ws.role === "phone" && msg.type === "choice") {
      // { type:"choice", voteId, option }
      castVote(ws.clientId, msg.voteId, msg.option);
    } else if (ws.role === "phone" && msg.type === "press") {
      press(ws.clientId);
    }
  });

  ws.on("close", broadcastState);
});

// ---------- Start ----------

server.listen(PORT, () => {
  console.log(`Stemserver draait op poort ${PORT}`);
  console.log(`  Telefoons:  ${JOIN_URL}`);
  console.log(`  Host/test:  ${JOIN_URL}host`);
  const others = lanIps().slice(1);
  if (!process.env.PUBLIC_URL && others.length)
    console.log(`  (Andere IP's: ${others.join(", ")}. Verkeerd? Zet PUBLIC_URL, zie UNITY-KOPPELING.md)`);
});
