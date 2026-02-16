import type { Express } from "express";
import { createServer, type Server } from "http";
import { storage, type DataSource } from "./storage";
import multer from "multer";
import { parse } from "csv-parse/sync";
import express from "express";
import path from "path";

const DOTNET_API_URL = "http://localhost:5001";
const upload = multer({ storage: multer.memoryStorage(), limits: { fileSize: 10 * 1024 * 1024 } });

const PIN_REGEX = /^\d{2}-\d{2}-\d{3}-\d{3}-\d{4}$/;
const RAW_PIN_REGEX = /^\d{14}$/;

function formatPin(raw: string): string {
  const digits = raw.replace(/\D/g, "");
  if (digits.length === 14) {
    return `${digits.slice(0, 2)}-${digits.slice(2, 4)}-${digits.slice(4, 7)}-${digits.slice(7, 10)}-${digits.slice(10, 14)}`;
  }
  return raw;
}

function extractPinsFromText(text: string): string[] {
  const pins = new Set<string>();
  const lines = text.split(/[\r\n]+/);

  for (const line of lines) {
    const fields = line.split(/[,\t;|]/);
    for (const field of fields) {
      const trimmed = field.trim().replace(/^["']|["']$/g, "");
      if (PIN_REGEX.test(trimmed)) {
        pins.add(trimmed);
      } else if (RAW_PIN_REGEX.test(trimmed)) {
        pins.add(formatPin(trimmed));
      } else {
        const digits = trimmed.replace(/\D/g, "");
        if (digits.length === 14) {
          const formatted = formatPin(digits);
          if (PIN_REGEX.test(formatted)) {
            pins.add(formatted);
          }
        }
      }
    }
  }

  return Array.from(pins);
}

const SOURCE_MAP: Record<DataSource, string> = {
  "tax-portal": "/api/cook/tax-portal-data",
  "clerk": "/api/cook/clerk-data",
  "recorder": "/api/cook/recorder-data",
  "cookviewer": "/api/cook/cookviewer-data",
};

async function proxyToDoNet(endpoint: string, pin: string): Promise<{ status: number; data: unknown }> {
  try {
    const response = await fetch(`${DOTNET_API_URL}${endpoint}?pin=${encodeURIComponent(pin)}`);
    const data = await response.json();
    return { status: response.status, data };
  } catch (error) {
    return {
      status: 502,
      data: {
        success: false,
        error: ".NET API is not available. Please ensure the 'Start .NET API' workflow is running.",
        code: "DOTNET_UNAVAILABLE"
      }
    };
  }
}

type CacheWriter = (pin: string, source: DataSource, data: unknown, error?: string) => Promise<void>;

async function fetchAndCacheSource(pin: string, source: DataSource, cacheWriter: CacheWriter): Promise<void> {
  const endpoint = SOURCE_MAP[source];
  const { status, data } = await proxyToDoNet(endpoint, pin);
  const response = data as { success?: boolean; data?: unknown; error?: string };

  if (response.success && response.data) {
    await cacheWriter(pin, source, response.data);
  } else {
    await cacheWriter(pin, source, null, response.error || `HTTP ${status}`);
  }
}

async function fetchAllSourcesForPin(pin: string, cacheWriter: CacheWriter): Promise<{ success: boolean; errors: string[] }> {
  const errors: string[] = [];
  const sources: DataSource[] = ["tax-portal", "clerk", "recorder", "cookviewer"];

  await Promise.all(
    sources.map(async (source) => {
      try {
        await fetchAndCacheSource(pin, source, cacheWriter);
      } catch (err: any) {
        errors.push(`${source}: ${err.message}`);
        await cacheWriter(pin, source, null, err.message);
      }
    })
  );

  return { success: errors.length === 0, errors };
}

const activeJobs = new Set<string>();

async function processImportJob(jobId: string): Promise<void> {
  if (activeJobs.has(jobId)) return;
  activeJobs.add(jobId);

  try {
    await storage.updateImportJobStatus(jobId, "processing");
    const BATCH_SIZE = 5;
    const DELAY_BETWEEN_BATCHES_MS = 2000;

    while (true) {
      const pendingPins = await storage.getPendingPinsForJob(jobId, BATCH_SIZE);
      if (pendingPins.length === 0) break;

      await Promise.all(
        pendingPins.map(async (importPin) => {
          try {
            await storage.updateImportPinStatus(importPin.id, "fetching");
            const result = await fetchAllSourcesForPin(importPin.pin, storage.setCachedDataImport.bind(storage));
            if (result.errors.length === 0) {
              await storage.updateImportPinStatus(importPin.id, "complete");
            } else if (result.errors.length < 4) {
              await storage.updateImportPinStatus(importPin.id, "complete", result.errors.join("; "));
            } else {
              await storage.updateImportPinStatus(importPin.id, "error", result.errors.join("; "));
            }
          } catch (err: any) {
            await storage.updateImportPinStatus(importPin.id, "error", err.message);
          }
        })
      );

      await storage.updateImportJobCounts(jobId);

      if (pendingPins.length === BATCH_SIZE) {
        await new Promise((resolve) => setTimeout(resolve, DELAY_BETWEEN_BATCHES_MS));
      }
    }

    await storage.updateImportJobCounts(jobId);
    await storage.updateImportJobStatus(jobId, "complete");
  } catch (err: any) {
    await storage.updateImportJobStatus(jobId, "error");
  } finally {
    activeJobs.delete(jobId);
  }
}

const CACHE_MAX_AGE_HOURS = 24 * 7;

function isCacheStale(fetchedAt: Date): boolean {
  const ageMs = Date.now() - new Date(fetchedAt).getTime();
  return ageMs > CACHE_MAX_AGE_HOURS * 60 * 60 * 1000;
}

async function recoverStuckJobs(): Promise<void> {
  try {
    const jobs = await storage.getImportJobs();
    for (const job of jobs) {
      if (job.status === "processing" && !activeJobs.has(job.id)) {
        const pending = await storage.getPendingPinsForJob(job.id, 1);
        if (pending.length > 0) {
          console.log(`Recovering stuck import job ${job.id}`);
          processImportJob(job.id).catch(console.error);
        } else {
          await storage.updateImportJobCounts(job.id);
          await storage.updateImportJobStatus(job.id, "complete");
        }
      }
    }
  } catch (err) {
    console.error("Failed to recover stuck jobs:", err);
  }
}

export async function registerRoutes(
  httpServer: Server,
  app: Express
): Promise<Server> {
  setTimeout(recoverStuckJobs, 5000);

  app.use('/enterprise-drawer', express.static(path.join(process.cwd(), 'enterprise-drawer')));

  // Proxy Swagger UI and OpenAPI spec from .NET API (port 5001) through Express (port 5000)
  app.get('/api-docs', async (_req, res) => {
    try {
      const response = await fetch(`${DOTNET_API_URL}/index.html`);
      if (!response.ok) throw new Error(`Swagger UI not available (HTTP ${response.status})`);
      let html = await response.text();
      html = html.replace(/href="\.?\/?([^"]+\.css)"/g, 'href="/api-docs-assets/$1"');
      html = html.replace(/src="\.?\/?([^"]+\.js)"/g, 'src="/api-docs-assets/$1"');
      html = html.replace(/"url"\s*:\s*"\/swagger\//g, '"url":"/api-docs-assets/swagger/');
      res.setHeader('Content-Type', 'text/html');
      res.send(html);
    } catch {
      res.status(502).json({ error: '.NET API is not running. Start the "Start .NET API" workflow first.' });
    }
  });

  app.use('/api-docs-assets', async (req, res, next) => {
    if (req.method !== 'GET') { next(); return; }
    try {
      const assetPath = req.url.startsWith('/') ? req.url.slice(1) : req.url;
      const response = await fetch(`${DOTNET_API_URL}/${assetPath}`);
      if (!response.ok) { res.status(response.status).end(); return; }
      const contentType = response.headers.get('content-type');
      if (contentType) res.setHeader('Content-Type', contentType);
      const buffer = Buffer.from(await response.arrayBuffer());
      res.send(buffer);
    } catch {
      res.status(502).end();
    }
  });

  // Legacy raw HTML proxy endpoints
  app.get("/api/cook/tax-portal", async (req, res) => {
    const pin = req.query.pin as string;
    res.setHeader("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
    res.setHeader("Pragma", "no-cache");
    res.setHeader("Expires", "Tue, 01 Jan 2000 00:00:00 GMT");
    const { status, data } = await proxyToDoNet("/api/cook/tax-portal", pin);
    res.status(status).json(data);
  });

  app.get("/api/cook/county-clerk", async (req, res) => {
    const pin = req.query.pin as string;
    res.setHeader("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
    res.setHeader("Pragma", "no-cache");
    res.setHeader("Expires", "Tue, 01 Jan 2000 00:00:00 GMT");
    const { status, data } = await proxyToDoNet("/api/cook/county-clerk", pin);
    res.status(status).json(data);
  });

  app.get("/api/cook/treasurer", async (req, res) => {
    const pin = req.query.pin as string;
    res.setHeader("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
    res.setHeader("Pragma", "no-cache");
    res.setHeader("Expires", "Tue, 01 Jan 2000 00:00:00 GMT");
    const { status, data } = await proxyToDoNet("/api/cook/treasurer", pin);
    res.status(status).json(data);
  });

  app.get("/api/cook/recorder", async (req, res) => {
    const pin = req.query.pin as string;
    res.setHeader("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
    res.setHeader("Pragma", "no-cache");
    res.setHeader("Expires", "Tue, 01 Jan 2000 00:00:00 GMT");
    const { status, data } = await proxyToDoNet("/api/cook/recorder", pin);
    res.status(status).json(data);
  });

  app.get("/api/cook/assessor", async (req, res) => {
    const pin = req.query.pin as string;
    res.setHeader("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
    res.setHeader("Pragma", "no-cache");
    res.setHeader("Expires", "Tue, 01 Jan 2000 00:00:00 GMT");
    const { status, data } = await proxyToDoNet("/api/cook/assessor", pin);
    res.status(status).json(data);
  });

  // Structured data endpoints - serve from DB cache first, fall back to live
  async function handleCachedEndpoint(
    req: any, res: any, source: DataSource, dotnetEndpoint: string
  ) {
    const pin = req.query.pin as string;
    if (!pin || !PIN_REGEX.test(pin)) {
      return res.status(400).json({ success: false, error: "Invalid PIN format", code: "INVALID_PIN" });
    }

    const cached = await storage.getCachedData(pin, source);
    if (cached?.data && !isCacheStale(cached.fetchedAt)) {
      return res.json({ success: true, data: cached.data, cached: true, cachedAt: cached.fetchedAt });
    }

    const { status, data } = await proxyToDoNet(dotnetEndpoint, pin);
    const response = data as { success?: boolean; data?: unknown; error?: string };
    if (response.success && response.data) {
      storage.setCachedData(pin, source, response.data).catch(() => {});
    } else if (cached?.data) {
      return res.json({ success: true, data: cached.data, cached: true, cachedAt: cached.fetchedAt, stale: true });
    }
    res.status(status).json(data);
  }

  app.get("/api/cook/tax-portal-data", (req, res) => handleCachedEndpoint(req, res, "tax-portal", "/api/cook/tax-portal-data"));
  app.get("/api/cook/clerk-data", (req, res) => handleCachedEndpoint(req, res, "clerk", "/api/cook/clerk-data"));
  app.get("/api/cook/recorder-data", (req, res) => handleCachedEndpoint(req, res, "recorder", "/api/cook/recorder-data"));
  app.get("/api/cook/cookviewer-data", (req, res) => handleCachedEndpoint(req, res, "cookviewer", "/api/cook/cookviewer-data"));

  app.get("/api/cook/google-maps-data", async (req, res) => {
    const lat = req.query.lat as string;
    const lon = req.query.lon as string;
    res.setHeader("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
    res.setHeader("Pragma", "no-cache");
    try {
      const response = await fetch(`${DOTNET_API_URL}/api/cook/google-maps-data?lat=${encodeURIComponent(lat)}&lon=${encodeURIComponent(lon)}`);
      const data = await response.json();
      res.status(response.status).json(data);
    } catch {
      res.status(502).json({ success: false, error: ".NET API is not available", code: "DOTNET_UNAVAILABLE" });
    }
  });

  app.get("/api/cook/property-summary", async (req, res) => {
    const pin = req.query.pin as string;
    res.setHeader("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
    res.setHeader("Pragma", "no-cache");
    res.setHeader("Expires", "Tue, 01 Jan 2000 00:00:00 GMT");
    const { status, data } = await proxyToDoNet("/api/cook/property-summary", pin);
    res.status(status).json(data);
  });

  // Cache management
  app.post("/api/cook/clear-cache", async (req, res) => {
    const pin = req.query.pin as string | undefined;
    try {
      const url = pin
        ? `${DOTNET_API_URL}/api/cook/clear-cache?pin=${encodeURIComponent(pin)}`
        : `${DOTNET_API_URL}/api/cook/clear-cache`;
      const response = await fetch(url, { method: "POST" });
      const data = await response.json();
      res.status(response.status).json(data);
    } catch {
      res.status(502).json({ success: false, error: ".NET API is not available", code: "DOTNET_UNAVAILABLE" });
    }
  });

  // Health check
  app.get("/api/health", async (req, res) => {
    try {
      const response = await fetch(`${DOTNET_API_URL}/api/health`);
      const data = await response.json();
      res.json({ ...data, proxy: "express", backend: ".NET" });
    } catch {
      res.status(502).json({
        status: "degraded",
        error: ".NET API is not available",
        timestamp: new Date().toISOString(),
      });
    }
  });

  // === Import Job Endpoints ===

  app.post("/api/import/upload", upload.single("file"), async (req, res) => {
    try {
      const file = req.file;
      if (!file) {
        return res.status(400).json({ success: false, error: "No file uploaded" });
      }

      const text = file.buffer.toString("utf-8");
      const pins = extractPinsFromText(text);

      if (pins.length === 0) {
        return res.status(400).json({
          success: false,
          error: "No valid PINs found in file. PINs should be in format XX-XX-XXX-XXX-XXXX or 14-digit numbers.",
        });
      }

      const job = await storage.createImportJob(file.originalname, pins);

      processImportJob(job.id).catch((err) => {
        console.error(`Import job ${job.id} failed:`, err);
      });

      res.json({
        success: true,
        data: {
          jobId: job.id,
          filename: file.originalname,
          totalPins: pins.length,
          samplePins: pins.slice(0, 5),
          status: "processing",
        },
      });
    } catch (err: any) {
      res.status(500).json({ success: false, error: err.message });
    }
  });

  app.get("/api/import/pins", async (req, res) => {
    try {
      const pins = await storage.getImportedPins();
      res.json({ success: true, data: pins });
    } catch (err: any) {
      res.status(500).json({ success: false, error: err.message });
    }
  });

  app.get("/api/import/jobs", async (req, res) => {
    try {
      const jobs = await storage.getImportJobs();
      res.json({ success: true, data: jobs });
    } catch (err: any) {
      res.status(500).json({ success: false, error: err.message });
    }
  });

  app.get("/api/import/jobs/:jobId", async (req, res) => {
    try {
      const job = await storage.getImportJob(req.params.jobId);
      if (!job) {
        return res.status(404).json({ success: false, error: "Job not found" });
      }

      const limit = Math.min(parseInt(req.query.limit as string) || 100, 500);
      const offset = parseInt(req.query.offset as string) || 0;
      const pins = await storage.getImportJobPins(req.params.jobId, limit, offset);
      const pinCounts = await storage.getImportJobPinCounts(req.params.jobId);

      res.json({
        success: true,
        data: {
          ...job,
          pinCounts,
          pagination: { limit, offset, total: pinCounts.total },
          pins: pins.map((p) => ({
            pin: p.pin,
            status: p.status,
            error: p.error,
            fetchedAt: p.fetchedAt,
          })),
        },
      });
    } catch (err: any) {
      res.status(500).json({ success: false, error: err.message });
    }
  });

  app.get("/api/pins/:pin/bids", async (req, res) => {
    try {
      const pin = req.params.pin;
      if (!PIN_REGEX.test(pin)) {
        return res.status(400).json({ success: false, error: "Invalid PIN format" });
      }
      const bids = await storage.getPinBids(pin);
      res.json({
        success: true,
        data: {
          pin,
          bid: bids?.bid || null,
          overbid: bids?.overbid || null,
          updatedAt: bids?.updatedAt || null,
        },
      });
    } catch (err: any) {
      res.status(500).json({ success: false, error: err.message });
    }
  });

  app.put("/api/pins/:pin/bids", async (req, res) => {
    try {
      const pin = req.params.pin;
      if (!PIN_REGEX.test(pin)) {
        return res.status(400).json({ success: false, error: "Invalid PIN format" });
      }
      const { bid, overbid } = req.body;
      const bidVal = bid != null && bid !== "" ? String(bid) : null;
      const overbidVal = overbid != null && overbid !== "" ? String(overbid) : null;
      if (bidVal !== null && isNaN(Number(bidVal))) {
        return res.status(400).json({ success: false, error: "Bid must be a valid number" });
      }
      if (overbidVal !== null && isNaN(Number(overbidVal))) {
        return res.status(400).json({ success: false, error: "Overbid must be a valid number" });
      }
      const result = await storage.upsertPinBids(pin, bidVal, overbidVal);
      res.json({
        success: true,
        data: {
          pin: result.pin,
          bid: result.bid,
          overbid: result.overbid,
          updatedAt: result.updatedAt,
        },
      });
    } catch (err: any) {
      res.status(500).json({ success: false, error: err.message });
    }
  });

  app.get("/api/cache/stats", async (req, res) => {
    try {
      const jobs = await storage.getImportJobs();
      const cacheStats = await storage.getCacheStats();
      const totalJobs = jobs.length;
      const activeJobCount = jobs.filter((j) => j.status === "processing").length;

      res.json({
        success: true,
        data: {
          totalJobs,
          activeJobs: activeJobCount,
          ...cacheStats,
          jobs: jobs.slice(0, 10),
        },
      });
    } catch (err: any) {
      res.status(500).json({ success: false, error: err.message });
    }
  });

  return httpServer;
}
