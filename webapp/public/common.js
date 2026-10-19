// Gedeeld door telefoon- en host-pagina: WebSocket met automatisch opnieuw verbinden.

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

function connect(role, onState, onStatus) {
  const clientId = getClientId();
  let ws;

  function open() {
    const proto = location.protocol === "https:" ? "wss" : "ws";
    ws = new WebSocket(`${proto}://${location.host}/ws`);
    ws.onopen = () => {
      ws.send(JSON.stringify({ type: "hello", role, clientId }));
      onStatus(true);
    };
    ws.onmessage = (e) => {
      const msg = JSON.parse(e.data);
      if (msg.type === "state") onState(msg);
    };
    ws.onclose = () => {
      onStatus(false);
      setTimeout(open, 1000);
    };
  }
  open();

  return {
    send(msg) {
      if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(msg));
    },
  };
}

const ENDING_NAMES = {
  held: "De held van de straat",
  weg: "Net op tijd weg",
  cadeau: "Het cadeau is weg",
};
const ENDING_ORDER = ["held", "weg", "cadeau"];

function winnerLabel(result) {
  const o = result.options.find((o) => o.id === result.winner);
  return o ? o.label : "";
}
