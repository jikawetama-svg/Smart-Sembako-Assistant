const http = require("http");
const fs = require("fs");
const path = require("path");
const os = require("os");
const pino = require("pino");
const QRCode = require("qrcode");
const {
  default: makeWASocket,
  DisconnectReason,
  downloadMediaMessage,
  fetchLatestBaileysVersion,
  isLidUser,
  jidDecode,
  jidNormalizedUser,
  useMultiFileAuthState
} = require("@whiskeysockets/baileys");

const logger = pino({ level: process.env.SSA_LOG_LEVEL || "info" });
const sidecarBuildTag = "baileys-sidecar-2026-05-22-upsert-empty-replay-guard";

const port = Number.parseInt(process.env.SSA_LOCAL_API_PORT || "8091", 10);
const sessionPath = path.resolve(process.env.SSA_SESSION_PATH || path.join(process.cwd(), "session"));
const mediaPath = path.resolve(process.env.SSA_MEDIA_PATH || path.join(path.dirname(sessionPath), "baileys-media"));
const desktopInboundUrl = process.env.SSA_DESKTOP_INBOUND_URL || "http://localhost:8090/baileys/events/inbound";
const pairingCodeTtlMs = Math.max(30, Number.parseInt(process.env.SSA_PAIRING_CODE_TTL_SECONDS || "120", 10)) * 1000;
const qrCodeTtlMs = Math.max(30, Number.parseInt(process.env.SSA_QR_CODE_TTL_SECONDS || "60", 10)) * 1000;
const pairingRetryCooldownMs = Math.max(15, Number.parseInt(process.env.SSA_PAIRING_RETRY_COOLDOWN_SECONDS || "30", 10)) * 1000;
const pairingRateLimitCooldownMs = Math.max(1, Number.parseInt(process.env.SSA_PAIRING_RATE_LIMIT_COOLDOWN_MINUTES || "2", 10)) * 60 * 1000;
const maxPairingRequestsPerHour = Math.max(1, Number.parseInt(process.env.SSA_MAX_PAIRING_REQUESTS_PER_HOUR || "8", 10));
const inboundStaleToleranceMs = Math.max(0, Number.parseInt(process.env.SSA_INBOUND_STALE_TOLERANCE_SECONDS || "120", 10)) * 1000;
const startupGraceMs = Math.max(0, Number.parseInt(process.env.SSA_STARTUP_GRACE_SECONDS || "30", 10)) * 1000;
const sidecarStartedAtMs = Date.now();
const sidecarStartedAt = new Date(sidecarStartedAtMs).toISOString();
const appInstanceId = process.env.SSA_APP_INSTANCE_ID || "";
const machineName = process.env.SSA_MACHINE_NAME || os.hostname();
const browserDescription = ["Chrome", "Windows", "10"];
const authorizedPhones = parseAuthorizedPhones(process.env.SSA_AUTHORIZED_NUMBERS || "");

let sock = null;
let pairingCode = null;
let pairingRequestedFor = null;
let pairingCodeCreatedAt = null;
let pairingCodeExpiresAt = null;
let qrCode = null;
let qrDataUrl = null;
let qrCodeCreatedAt = null;
let qrCodeExpiresAt = null;
let qrInProgress = false;
let lastPairingRequestAt = null;
let pairingRequestCountWindow = [];
let pairingRequestLock = false;
let lastSeen = null;
let lastError = null;
let paired = false;
let connected = false;
let bootPromise = null;
let resetInProgress = false;
let pairingInProgress = false;
let connectionState = "starting";
let lastDisconnectStatusCode = null;
let lastDisconnectReason = null;
let baileysVersion = null;
let socketGeneration = 0;
let shuttingDown = false;
let reconnectAttempt = 0;
let reconnectTimer = null;
const subscribedJids = new Set();

fs.mkdirSync(sessionPath, { recursive: true });
fs.mkdirSync(mediaPath, { recursive: true });

async function bootSocket(force = false) {
  if (bootPromise && !force) {
    return bootPromise;
  }

  const generation = socketGeneration;
  const promise = (async () => {
    const { state, saveCreds } = await useMultiFileAuthState(sessionPath);
    const { version } = await fetchLatestBaileysVersion();
    if (generation !== socketGeneration) {
      logger.warn({ generation, activeGeneration: socketGeneration }, "Ignoring stale socket boot");
      return;
    }

    baileysVersion = version;
    connectionState = "connecting";
    subscribedJids.clear();
    clearReconnectTimer();
    logger.info({ version, browser: browserDescription, sessionPath, generation, sidecarBuildTag }, "Booting Baileys socket");

    const socket = makeWASocket({
      auth: state,
      version,
      logger,
      printQRInTerminal: false,
      browser: browserDescription
    });
    sock = socket;

    socket.ev.on("creds.update", (...args) => {
      if (generation !== socketGeneration) {
        return;
      }

      saveCreds(...args);
    });

    socket.ev.on("connection.update", async (update) => {
      if (generation !== socketGeneration) {
        logger.warn({ generation, activeGeneration: socketGeneration }, "Ignoring stale connection update");
        return;
      }

      lastSeen = new Date().toISOString();
      connectionState = update.connection || connectionState;

      if (update.connection === "open") {
        logger.info("Baileys socket connected");
        connected = true;
        reconnectAttempt = 0;
        paired = true;
        pairingCode = null;
        pairingRequestedFor = null;
        pairingCodeCreatedAt = null;
        pairingCodeExpiresAt = null;
        clearQrCode();
        lastError = null;
        pairingInProgress = false;
        lastDisconnectStatusCode = null;
        lastDisconnectReason = null;
        prefetchAuthorizedLidMappings().catch((error) => {
          logger.warn({ err: error }, "Failed to prefetch authorized LID mappings");
        });
      }

      if (update.qr) {
        qrCode = update.qr;
        qrCodeCreatedAt = new Date().toISOString();
        qrCodeExpiresAt = new Date(Date.now() + qrCodeTtlMs).toISOString();
        qrInProgress = true;
        lastError = null;
        qrDataUrl = await QRCode.toDataURL(update.qr, {
          errorCorrectionLevel: "M",
          margin: 2,
          width: 320
        });
        logger.info({ qrCodeExpiresAt, generation }, "QR pairing code created");
      }

      if (update.connection === "close") {
        connected = false;
        const statusCode = update.lastDisconnect?.error?.output?.statusCode;
        lastDisconnectStatusCode = statusCode || null;
        lastDisconnectReason = update.lastDisconnect?.error?.message || update.lastDisconnect?.error?.output?.payload?.message || "Connection Closed";
        logger.warn({ statusCode: lastDisconnectStatusCode, reason: lastDisconnectReason }, "Baileys socket closed");
        if (pairingInProgress && !lastError) {
          lastError = lastDisconnectReason;
        }
        if (shouldReconnect(statusCode) && !resetInProgress && !shuttingDown) {
          scheduleReconnect(generation);
        } else if (!resetInProgress && !shuttingDown) {
          paired = false;
          pairingCode = null;
          pairingRequestedFor = null;
          pairingCodeCreatedAt = null;
          pairingCodeExpiresAt = null;
          clearQrCode();
          lastError = "Session logged out. Start pairing again.";
          pairingInProgress = false;
        }
      }
    });

    socket.ev.on("messages.upsert", async ({ messages, type }) => {
      if (generation !== socketGeneration) {
        return;
      }

      if (type !== "notify") {
        logger.info({ type: type || "missing", count: messages?.length || 0 }, "Ignoring non-live Baileys message upsert");
        return;
      }

      for (const message of messages || []) {
        if (!message.message) {
          continue;
        }

        if (isStaleInboundMessage(message)) {
          logger.info({
            messageId: message.key.id,
            remoteJid: message.key.remoteJid,
            timestamp: formatMessageTimestamp(message),
            sidecarStartedAt,
            toleranceMs: inboundStaleToleranceMs
          }, "Ignoring stale Baileys inbound message");
          continue;
        }

        if (message.key.fromMe) {
          logger.info({ messageId: message.key.id, remoteJid: message.key.remoteJid }, "Ignoring own outbound message");
          continue;
        }

        const sender = await resolveSenderIdentity(message);
        const text =
          message.message.conversation ||
          message.message.extendedTextMessage?.text ||
          message.message.imageMessage?.caption ||
          message.message.documentMessage?.caption ||
          message.message.videoMessage?.caption ||
          "";
        const media = await downloadInboundMedia(message);

        if (!text.trim() && !media.filePath) {
          logger.info({
            messageId: message.key.id,
            remoteJid: message.key.remoteJid,
            rawJid: sender.rawJid
          }, "Ignoring empty or unsupported inbound message");
          continue;
        }

        const payload = {
          senderId: sender.senderId,
          senderName: message.pushName || "",
          text,
          caption: media.caption || text,
          mediaUrl: media.filePath,
          mediaMimeType: media.mimeType,
          fileName: media.fileName,
          messageId: message.key.id || "",
          rawSenderJid: sender.rawJid,
          resolvedSenderJid: sender.resolvedJid,
          appInstanceId,
          machineName,
          sidecarBuildTag,
          upsertType: "notify",
          originalUpsertType: type || "",
          sidecarStartedAt,
          receivedAt: new Date().toISOString(),
          messageTimestampMs: getMessageTimestampMs(message),
          remoteJid: message.key.remoteJid || "",
          fromMe: message.key.fromMe === true,
          timestamp: formatMessageTimestamp(message) || new Date().toISOString()
        };

        try {
          sendPresence(sender.senderId, "composing").catch((error) => {
            logger.warn({ err: error, senderId: sender.senderId }, "Failed to send typing presence");
          });
          await postJson(desktopInboundUrl, payload);
          lastError = null;
          logger.info({
            senderId: sender.senderId,
            rawJid: sender.rawJid,
            resolvedJid: sender.resolvedJid,
            messageId: payload.messageId,
            appInstanceId,
            sidecarBuildTag,
            mediaMimeType: payload.mediaMimeType || null,
            inboundUrl: desktopInboundUrl
          }, "Forwarded inbound message to desktop app");
        } catch (error) {
          lastError = error.message;
          logger.error({
            err: error,
            senderId: sender.senderId,
            rawJid: sender.rawJid,
            resolvedJid: sender.resolvedJid,
            messageId: payload.messageId,
            inboundUrl: desktopInboundUrl
          }, "Failed to forward inbound message to desktop app");
        }
      }
    });
  })();
  bootPromise = promise;

  try {
    await promise;
  } finally {
    if (bootPromise === promise) {
      bootPromise = null;
    }
  }
}

async function ensurePairingCode(phoneNumber) {
  if (!sock) {
    await bootSocket();
  }

  const normalized = normalizePhone(phoneNumber);
  if (!normalized) {
    throw new Error("Nomor pairing belum valid.");
  }

  if (paired && connected) {
    return null;
  }

  pruneExpiredPairingCode();
  if (pairingCode && pairingRequestedFor === normalized && pairingCodeExpiresAt && Date.now() < Date.parse(pairingCodeExpiresAt)) {
    logger.info({ phoneNumber: normalized, pairingCodeExpiresAt }, "Reusing active pairing code");
    return pairingCode;
  }

  if (pairingRequestLock) {
    throw createStructuredError("pairing-in-progress", "Pairing code sedang dibuat.", 5);
  }

  enforcePairingRateLimit();

  await waitForPairingReady();
  pairingRequestedFor = normalized;
  pairingInProgress = true;
  pairingRequestLock = true;
  try {
    recordPairingRequest();
    logger.info({ phoneNumber: normalized }, "Requesting new pairing code");
    pairingCode = await withTimeout(
      sock.requestPairingCode(normalized),
      30000,
      "Pairing code request timeout. Coba generate kode lagi."
    );
    pairingCodeCreatedAt = new Date().toISOString();
    pairingCodeExpiresAt = new Date(Date.now() + pairingCodeTtlMs).toISOString();
    lastError = null;
    logger.info({ phoneNumber: normalized, pairingCodeExpiresAt }, "Pairing code created");
    return pairingCode;
  } catch (error) {
    pairingInProgress = false;
    throw error;
  } finally {
    pairingRequestLock = false;
  }
}

function withTimeout(promise, timeoutMs, message) {
  let timer = null;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(createStructuredError("timeout", message, 30)), timeoutMs);
  });

  return Promise.race([promise, timeout]).finally(() => {
    if (timer) {
      clearTimeout(timer);
    }
  });
}

async function ensureQrCode(resetSessionFirst = false) {
  if (paired && connected) {
    return null;
  }

  if (resetSessionFirst) {
    await resetSession();
  } else if (!sock) {
    await bootSocket();
  }

  pruneExpiredQrCode();
  if (qrDataUrl && qrCodeExpiresAt && Date.now() < Date.parse(qrCodeExpiresAt)) {
    logger.info({ qrCodeExpiresAt }, "Reusing active QR pairing code");
    return qrDataUrl;
  }

  qrInProgress = true;
  for (let attempt = 0; attempt < 20; attempt += 1) {
    pruneExpiredQrCode();
    if (qrDataUrl && qrCodeExpiresAt && Date.now() < Date.parse(qrCodeExpiresAt)) {
      return qrDataUrl;
    }

    if (connected || paired) {
      return null;
    }

    if (connectionState === "close" && lastDisconnectStatusCode === DisconnectReason.loggedOut) {
      throw createStructuredError("logged-out", "Session logged out. Reset session and request a new QR code.", 30);
    }

    await delay(1000);
  }

  qrInProgress = false;
  throw createStructuredError("qr-timeout", "QR pairing belum tersedia. Reset sesi lokal lalu coba QR lagi.", 30);
}

function shouldReconnect(statusCode) {
  return statusCode !== DisconnectReason.loggedOut &&
         statusCode !== DisconnectReason.forbidden &&
         statusCode !== 403;
}

function getReconnectDelayMs() {
  reconnectAttempt += 1;
  if (reconnectAttempt === 1) {
    return 3000;
  }
  if (reconnectAttempt === 2) {
    return 6000;
  }
  if (reconnectAttempt === 3) {
    return 12000;
  }
  if (reconnectAttempt === 4) {
    return 30000;
  }

  return 60000;
}

function scheduleReconnect(generation) {
  clearReconnectTimer();
  const delayMs = getReconnectDelayMs();
  logger.warn({ reconnectAttempt, delayMs }, "Scheduling Baileys reconnect with backoff");
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    if (generation !== socketGeneration || shuttingDown || resetInProgress) {
      return;
    }

    bootSocket(true).catch((error) => {
      lastError = error.message;
    });
  }, delayMs);
}

function clearReconnectTimer() {
  if (!reconnectTimer) {
    return;
  }

  clearTimeout(reconnectTimer);
  reconnectTimer = null;
}

function getMessageTimestampMs(message) {
  if (!message?.messageTimestamp) {
    return Date.now();
  }

  const seconds = Number(message.messageTimestamp);
  if (!Number.isFinite(seconds) || seconds <= 0) {
    return Date.now();
  }

  return seconds * 1000;
}

function formatMessageTimestamp(message) {
  const timestampMs = getMessageTimestampMs(message);
  return Number.isFinite(timestampMs)
    ? new Date(timestampMs).toISOString()
    : null;
}

function isStaleInboundMessage(message) {
  const timestampMs = getMessageTimestampMs(message);
  if (Date.now() < sidecarStartedAtMs + startupGraceMs && timestampMs <= sidecarStartedAtMs) {
    return true;
  }

  return timestampMs < sidecarStartedAtMs - inboundStaleToleranceMs;
}

function pruneExpiredPairingCode() {
  if (!pairingCodeExpiresAt || Date.now() < Date.parse(pairingCodeExpiresAt)) {
    return;
  }

  logger.warn({ pairingRequestedFor, pairingCodeCreatedAt, pairingCodeExpiresAt }, "Pairing code expired before connection opened");
  pairingCode = null;
  pairingCodeCreatedAt = null;
  pairingCodeExpiresAt = null;
  pairingInProgress = false;
  if (!connected && !paired && !lastError) {
    lastError = "Pairing code expired before WhatsApp connected. Reset session and request a new code.";
  }
}

function pruneExpiredQrCode() {
  if (!qrCodeExpiresAt || Date.now() < Date.parse(qrCodeExpiresAt)) {
    return;
  }

  logger.warn({ qrCodeCreatedAt, qrCodeExpiresAt }, "QR pairing code expired before connection opened");
  clearQrCode();
  if (!connected && !paired && !lastError) {
    lastError = "QR pairing expired before WhatsApp connected. Request a new QR code.";
  }
}

function clearQrCode() {
  qrCode = null;
  qrDataUrl = null;
  qrCodeCreatedAt = null;
  qrCodeExpiresAt = null;
  qrInProgress = false;
}

function enforcePairingRateLimit() {
  const now = Date.now();
  pairingRequestCountWindow = pairingRequestCountWindow.filter((entry) => now - entry < 60 * 60 * 1000);

  if (lastPairingRequestAt) {
    const nextAllowedAt = lastPairingRequestAt + pairingRetryCooldownMs;
    if (now < nextAllowedAt) {
      const retryAfterSeconds = Math.ceil((nextAllowedAt - now) / 1000);
      logger.warn({ retryAfterSeconds }, "Pairing request rejected by cooldown");
      throw createStructuredError("rate-limited", "Permintaan pairing terlalu cepat.", retryAfterSeconds);
    }
  }

  if (pairingRequestCountWindow.length >= maxPairingRequestsPerHour) {
    logger.warn({ maxPairingRequestsPerHour }, "Pairing request rejected by hourly cap");
    throw createStructuredError(
      "rate-limited",
      "Batas request pairing per jam tercapai.",
      Math.ceil(pairingRateLimitCooldownMs / 1000)
    );
  }
}

function recordPairingRequest() {
  const now = Date.now();
  lastPairingRequestAt = now;
  pairingRequestCountWindow.push(now);
}

async function waitForPairingReady() {
  for (let attempt = 0; attempt < 10; attempt += 1) {
    if (connected || connectionState === "connecting" || connectionState === "open") {
      return true;
    }

    if (connectionState === "close" && lastDisconnectReason) {
      throw createStructuredError("connection-closed", lastDisconnectReason);
    }

    await delay(1000);
  }

  throw createStructuredError("not-ready", "Socket belum siap untuk pairing.");
}

async function resetSession() {
  logger.warn({ sessionPath }, "Manual session reset requested");
  resetInProgress = true;
  socketGeneration += 1;
  pairingCode = null;
  pairingRequestedFor = null;
  pairingCodeCreatedAt = null;
  pairingCodeExpiresAt = null;
  clearQrCode();
  paired = false;
  connected = false;
  reconnectAttempt = 0;
  clearReconnectTimer();
  lastError = null;
  pairingInProgress = false;
  connectionState = "resetting";
  lastDisconnectStatusCode = null;
  lastDisconnectReason = null;

  try {
    const activeSocket = sock;
    sock = null;
    if (activeSocket?.logout) {
      try {
        await activeSocket.logout();
      } catch (error) {
        logger.warn({ err: error }, "Failed to logout socket before reset; continuing local session cleanup");
      }
    }

    if (activeSocket?.end) {
      try {
        activeSocket.end(new Error("Manual session reset"));
      } catch (error) {
        logger.warn(error, "Failed to close socket gracefully during reset");
      }
    }

    subscribedJids.clear();
    if (fs.existsSync(sessionPath)) {
      fs.rmSync(sessionPath, { recursive: true, force: true });
    }
    fs.mkdirSync(sessionPath, { recursive: true });
    await bootSocket(true);
  } finally {
    resetInProgress = false;
  }
}

async function postJson(url, payload) {
  const body = JSON.stringify(payload);
  const parsed = new URL(url);

  return new Promise((resolve, reject) => {
    const req = http.request(
      {
        hostname: parsed.hostname,
        port: parsed.port,
        path: parsed.pathname,
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Content-Length": Buffer.byteLength(body)
        }
      },
      (res) => {
        let raw = "";
        res.on("data", (chunk) => {
          raw += chunk;
        });
        res.on("end", () => {
          if (res.statusCode >= 200 && res.statusCode < 300) {
            resolve(raw);
            return;
          }

          reject(new Error(`HTTP ${res.statusCode}: ${raw}`));
        });
      }
    );

    req.on("error", reject);
    req.write(body);
    req.end();
  });
}

async function downloadInboundMedia(message) {
  const image = message.message?.imageMessage || null;
  const document = message.message?.documentMessage || null;
  const documentMime = document?.mimetype || "";
  const isImageDocument = document && /^image\//i.test(documentMime);
  const mediaMessage = image || (isImageDocument ? document : null);

  if (!mediaMessage) {
    return {
      filePath: null,
      mimeType: null,
      fileName: null,
      caption: null
    };
  }

  try {
    const buffer = await downloadMediaMessage(message, "buffer", {}, { logger });
    if (!buffer || buffer.length === 0) {
      throw new Error("Downloaded media buffer is empty.");
    }

    const mimeType = mediaMessage.mimetype || "image/jpeg";
    const extension = inferExtension(mimeType, document?.fileName);
    const fileName = sanitizeFileName(document?.fileName) || `wa_${Date.now()}_${message.key.id || "media"}${extension}`;
    const filePath = path.join(mediaPath, fileName);
    fs.writeFileSync(filePath, buffer);
    logger.info({
      fileName,
      filePath,
      mimeType,
      size: buffer.length,
      messageId: message.key.id
    }, "Downloaded inbound media");

    return {
      filePath,
      mimeType,
      fileName,
      caption: mediaMessage.caption || null
    };
  } catch (error) {
    lastError = error.message;
    logger.error({ err: error, messageId: message.key.id }, "Failed to download inbound media");
    return {
      filePath: null,
      mimeType: null,
      fileName: null,
      caption: mediaMessage.caption || null
    };
  }
}

function inferExtension(mimeType, originalName) {
  const originalExtension = path.extname(originalName || "");
  if (originalExtension) {
    return originalExtension;
  }

  switch ((mimeType || "").toLowerCase()) {
    case "image/png":
      return ".png";
    case "image/webp":
      return ".webp";
    case "image/heic":
      return ".heic";
    case "image/heif":
      return ".heif";
    default:
      return ".jpg";
  }
}

function sanitizeFileName(value) {
  if (!value) {
    return "";
  }

  const baseName = path.basename(value.toString()).replace(/[^a-zA-Z0-9._-]/g, "_");
  return baseName.length > 160 ? baseName.slice(-160) : baseName;
}

function normalizePhone(value) {
  const raw = (value || "").toString();
  const user = raw.split("@")[0].split(":")[0];
  return user.replace(/\D/g, "");
}

function parseAuthorizedPhones(value) {
  return [...new Set((value || "")
    .split(/[;,]/)
    .map(normalizePhone)
    .filter(Boolean))];
}

function normalizeJid(value) {
  if (!value) {
    return "";
  }

  try {
    return jidNormalizedUser(value);
  } catch {
    return value;
  }
}

function jidUser(value) {
  const decoded = jidDecode(value);
  return decoded?.user?.split(":")[0] || value?.split("@")[0]?.split(":")[0] || "";
}

function areSameJidUser(left, right) {
  return !!left && !!right && jidUser(left) === jidUser(right);
}

async function prefetchAuthorizedLidMappings() {
  if (!sock?.signalRepository?.lidMapping || authorizedPhones.length === 0) {
    return;
  }

  const pnJids = authorizedPhones.map((phone) => `${phone}@s.whatsapp.net`);
  await Promise.all(pnJids.map(async (pnJid) => {
    const lid = await sock.signalRepository.lidMapping.getLIDForPN(pnJid);
    if (lid) {
      logger.info({ pnJid, lid }, "Prefetched authorized LID mapping");
    }
  }));
}

async function resolveLidToPhoneJid(lidJid) {
  if (!sock?.signalRepository?.lidMapping || !isLidUser(lidJid)) {
    return null;
  }

  try {
    const mapped = await sock.signalRepository.lidMapping.getPNForLID(lidJid);
    if (mapped) {
      return normalizeJid(mapped);
    }
  } catch (error) {
    logger.warn({ err: error, lidJid }, "Failed to resolve PN for LID");
  }

  for (const phone of authorizedPhones) {
    const pnJid = `${phone}@s.whatsapp.net`;
    try {
      const mappedLid = await sock.signalRepository.lidMapping.getLIDForPN(pnJid);
      if (mappedLid && areSameJidUser(mappedLid, lidJid)) {
        return pnJid;
      }
    } catch (error) {
      logger.warn({ err: error, pnJid, lidJid }, "Failed to compare authorized LID mapping");
    }
  }

  return null;
}

async function resolveSenderIdentity(message) {
  const rawJid = message.key.participant || message.key.remoteJid || "";
  const normalizedJid = normalizeJid(rawJid);
  const resolvedJid = await resolveLidToPhoneJid(normalizedJid) || normalizedJid;

  return {
    rawJid,
    normalizedJid,
    resolvedJid,
    senderId: normalizePhone(resolvedJid || rawJid)
  };
}

async function sendMessage(recipient, text) {
  if (!sock) {
    throw new Error("Socket belum siap.");
  }

  if (!connected) {
    throw createStructuredError("not-ready", "WhatsApp lokal belum tersambung.");
  }

  const jid = `${normalizePhone(recipient)}@s.whatsapp.net`;
  const response = await sock.sendMessage(jid, { text });
  try {
    await sock.sendPresenceUpdate("paused", jid);
  } catch (error) {
    logger.warn({ err: error, recipient: normalizePhone(recipient) }, "Failed to pause typing presence after send");
  }
  lastSeen = new Date().toISOString();
  return response?.key?.id || null;
}

async function sendDocument(recipient, filePath, fileName, mimeType, caption) {
  if (!sock) {
    throw new Error("Socket belum siap.");
  }

  if (!connected) {
    throw createStructuredError("not-ready", "WhatsApp lokal belum tersambung.");
  }

  if (!filePath || !fs.existsSync(filePath)) {
    throw createStructuredError("missing-file", "File dokumen tidak ditemukan.");
  }

  const stat = fs.statSync(filePath);
  const maxBytes = 64 * 1024 * 1024;
  if (stat.size > maxBytes) {
    throw createStructuredError("file-too-large", "File terlalu besar untuk WhatsApp.", 0);
  }

  const jid = `${normalizePhone(recipient)}@s.whatsapp.net`;
  const resolvedFileName = sanitizeFileName(fileName) || path.basename(filePath);
  const response = await sock.sendMessage(jid, {
    document: fs.readFileSync(filePath),
    fileName: resolvedFileName,
    mimetype: mimeType || inferDocumentMimeType(filePath),
    caption: caption || ""
  });

  lastSeen = new Date().toISOString();
  logger.info({
    recipient: normalizePhone(recipient),
    fileName: resolvedFileName,
    size: stat.size,
    messageId: response?.key?.id || null
  }, "Sent outbound document");
  return response?.key?.id || null;
}

async function sendPresence(recipient, type = "composing") {
  if (!sock || !connected) {
    return false;
  }

  const normalized = normalizePhone(recipient);
  if (!normalized) {
    return false;
  }

  const jid = `${normalized}@s.whatsapp.net`;
  if (!subscribedJids.has(jid)) {
    await sock.presenceSubscribe(jid);
    subscribedJids.add(jid);
  }

  await sock.sendPresenceUpdate(type, jid);
  lastSeen = new Date().toISOString();
  return true;
}

async function shutdownGracefully() {
  shuttingDown = true;
  socketGeneration += 1;
  connectionState = "closing";
  clearReconnectTimer();
  subscribedJids.clear();
  const activeSocket = sock;
  sock = null;

  if (activeSocket?.sendPresenceUpdate) {
    try {
      await activeSocket.sendPresenceUpdate("unavailable");
    } catch (error) {
      logger.warn({ err: error }, "Failed to send unavailable presence during shutdown");
    }
  }

  if (activeSocket?.end) {
    try {
      activeSocket.end(new Error("Desktop app stopped Baileys sidecar"));
    } catch (error) {
      logger.warn({ err: error }, "Failed to close socket gracefully during shutdown");
    }
  }
}

function inferDocumentMimeType(filePath) {
  switch (path.extname(filePath || "").toLowerCase()) {
    case ".csv":
      return "text/csv";
    case ".zip":
      return "application/zip";
    case ".xlsx":
      return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    case ".xls":
      return "application/vnd.ms-excel";
    case ".pdf":
      return "application/pdf";
    case ".txt":
      return "text/plain";
    default:
      return "application/octet-stream";
  }
}

function readStatus() {
  pruneExpiredPairingCode();
  pruneExpiredQrCode();
  return {
    connected,
    paired,
    pairingCode,
    pairingCodeCreatedAt,
    pairingCodeExpiresAt,
    qrAvailable: !!qrDataUrl,
    qrDataUrl,
    qrCodeCreatedAt,
    qrCodeExpiresAt,
    qrInProgress,
    retryAfterSeconds: getPairingRetryAfterSeconds(),
    pairingInProgress,
    pairingRequestedFor,
    connectionState,
    lastDisconnectStatusCode,
    lastDisconnectReason,
    lastSeen,
    sessionPath,
    sidecarBuildTag,
    sidecarStartedAt,
    appInstanceId,
    machineName,
    baileysVersion,
    browser: browserDescription,
    error: lastError
  };
}

function readBotStatus() {
  return {
    instanceId: appInstanceId,
    machineName,
    connected,
    paired,
    sidecarBuildTag,
    sidecarStartedAt,
    pendingOutboundCount: 0,
    reconnectAttempt,
    subscribedJidCount: subscribedJids.size,
    uptimeSeconds: Math.floor((Date.now() - sidecarStartedAtMs) / 1000),
    connectionState,
    lastError
  };
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function createStructuredError(reason, message, retryAfterSeconds = null) {
  const error = new Error(message);
  error.reason = reason;
  error.retryAfterSeconds = retryAfterSeconds;
  return error;
}

function getPairingRetryAfterSeconds() {
  if (!lastPairingRequestAt) {
    return 0;
  }

  const nextAllowedAt = lastPairingRequestAt + pairingRetryCooldownMs;
  return Math.max(0, Math.ceil((nextAllowedAt - Date.now()) / 1000));
}

function inferReason(error) {
  const message = error?.message || "Unknown error";
  if (error?.reason) {
    return error.reason;
  }
  if (/logged out/i.test(message)) {
    return "logged-out";
  }
  if (/429|rate/i.test(message)) {
    return "rate-limited";
  }
  if (/405|Connection Failure/i.test(message)) {
    return "upstream-failure";
  }
  if (/Connection Closed|Precondition Required/i.test(message)) {
    return "connection-closed";
  }
  if (/not ready|belum siap/i.test(message)) {
    return "not-ready";
  }
  return "upstream-failure";
}

function json(res, statusCode, payload) {
  res.writeHead(statusCode, { "Content-Type": "application/json" });
  res.end(JSON.stringify(payload));
}

const server = http.createServer(async (req, res) => {
  let rawBody = "";
  req.on("data", (chunk) => {
    rawBody += chunk;
  });

  req.on("end", async () => {
    try {
      if (req.method === "GET" && req.url === "/health") {
        json(res, 200, readStatus());
        return;
      }

      if (req.method === "GET" && req.url === "/status_bot") {
        json(res, 200, readBotStatus());
        return;
      }

      if (req.method === "GET" && req.url === "/session/status") {
        json(res, 200, readStatus());
        return;
      }

      if (req.method === "POST" && req.url === "/session/reconnect") {
        await bootSocket(true);
        json(res, 200, {
          success: true,
          message: "Baileys reconnect dipicu.",
          connectionState
        });
        return;
      }

      if (req.method === "POST" && req.url === "/session/pairing/start") {
        const payload = rawBody ? JSON.parse(rawBody) : {};
        const code = await ensurePairingCode(payload.phoneNumber);
        json(res, 200, {
          success: true,
          message: code ? `Pairing code: ${code}` : "WhatsApp sudah terhubung.",
          pairingCode: code,
          pairingCodeExpiresAt,
          retryAfterSeconds: getPairingRetryAfterSeconds(),
          reason: null,
          connectionState,
          lastDisconnectStatusCode,
          lastDisconnectReason,
          pairingInProgress
        });
        return;
      }

      if (req.method === "POST" && req.url === "/session/qr/start") {
        const payload = rawBody ? JSON.parse(rawBody) : {};
        const dataUrl = await ensureQrCode(payload.resetSession === true);
        json(res, 200, {
          success: true,
          message: dataUrl ? "QR pairing siap." : "WhatsApp sudah terhubung.",
          qrAvailable: !!dataUrl,
          qrDataUrl: dataUrl,
          qrCodeExpiresAt,
          retryAfterSeconds: 0,
          connectionState,
          lastDisconnectStatusCode,
          lastDisconnectReason,
          pairingInProgress,
          qrInProgress
        });
        return;
      }

      if (req.method === "POST" && req.url === "/session/reset") {
        await resetSession();
        json(res, 200, {
          success: true,
          message: "Session reset complete.",
          retryAfterSeconds: Math.ceil(pairingRetryCooldownMs / 1000)
        });
        return;
      }

      if (req.method === "POST" && req.url === "/messages/send") {
        const payload = rawBody ? JSON.parse(rawBody) : {};
        const externalMessageId = await sendMessage(payload.recipient, payload.text);
        json(res, 200, {
          success: true,
          message: "Pesan Baileys terkirim.",
          externalMessageId
        });
        return;
      }

      if (req.method === "POST" && req.url === "/presence/typing") {
        const payload = rawBody ? JSON.parse(rawBody) : {};
        const type = payload.paused === true ? "paused" : "composing";
        const sent = await sendPresence(payload.recipient, type);
        json(res, 200, {
          success: true,
          message: sent ? "Presence terkirim." : "Presence dilewati karena socket belum siap.",
          connectionState
        });
        return;
      }

      if (req.method === "POST" && req.url === "/messages/send-document") {
        const payload = rawBody ? JSON.parse(rawBody) : {};
        const externalMessageId = await sendDocument(
          payload.recipient,
          payload.filePath,
          payload.fileName,
          payload.mimeType,
          payload.caption
        );
        json(res, 200, {
          success: true,
          message: "Dokumen Baileys terkirim.",
          externalMessageId
        });
        return;
      }

      if (req.method === "POST" && req.url === "/shutdown") {
        await shutdownGracefully();
        json(res, 200, {
          success: true,
          message: "Baileys sidecar shutdown started."
        });
        server.close(() => process.exit(0));
        setTimeout(() => process.exit(0), 1000).unref();
        return;
      }

      json(res, 404, { success: false, message: "Not found" });
    } catch (error) {
      lastError = error.message;
      json(res, 500, {
        success: false,
        message: error.message,
        reason: inferReason(error),
        retryAfterSeconds: error.retryAfterSeconds || getPairingRetryAfterSeconds(),
        connectionState,
        lastDisconnectStatusCode,
        lastDisconnectReason,
        pairingInProgress
      });
    }
  });
});

server.listen(port, async () => {
  logger.info(`Baileys sidecar listening on ${port}`);
  try {
    await bootSocket();
  } catch (error) {
    lastError = error.message;
    logger.error(error, "Failed to boot Baileys socket");
  }
});
