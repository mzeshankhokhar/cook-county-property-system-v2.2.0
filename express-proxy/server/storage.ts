import { eq, and, desc, sql, count } from "drizzle-orm";
import { type NodePgDatabase } from "drizzle-orm/node-postgres";
import { db, importDb } from "./db";
import {
  propertyCacheTable,
  importJobsTable,
  importPinsTable,
  pinBidsTable,
  type PropertyCache,
  type ImportJob,
  type ImportPin,
  type PinBid,
} from "@shared/schema";
import * as schema from "@shared/schema";

const SOURCES = ["tax-portal", "clerk", "recorder", "cookviewer"] as const;
export type DataSource = (typeof SOURCES)[number];

export interface IStorage {
  getCachedData(pin: string, source: DataSource): Promise<PropertyCache | null>;
  setCachedData(pin: string, source: DataSource, data: unknown, error?: string): Promise<void>;
  setCachedDataImport(pin: string, source: DataSource, data: unknown, error?: string): Promise<void>;
  getAllCachedDataForPin(pin: string): Promise<Record<DataSource, PropertyCache | null>>;

  createImportJob(filename: string, pins: string[]): Promise<ImportJob>;
  getImportJob(jobId: string): Promise<ImportJob | null>;
  getImportJobs(): Promise<ImportJob[]>;
  getPendingPinsForJob(jobId: string, limit: number): Promise<ImportPin[]>;
  updateImportPinStatus(pinId: string, status: string, error?: string): Promise<void>;
  updateImportJobCounts(jobId: string): Promise<void>;
  updateImportJobStatus(jobId: string, status: string): Promise<void>;
  getImportJobPins(jobId: string, limit?: number, offset?: number): Promise<ImportPin[]>;
  getImportJobPinCounts(jobId: string): Promise<{ total: number; pending: number; fetching: number; complete: number; error: number }>;
  getCacheStats(): Promise<{ totalCachedPins: number; totalCacheRows: number; oldestEntry: Date | null; newestEntry: Date | null }>;
  getImportedPins(): Promise<string[]>;
  getPinBids(pin: string): Promise<PinBid | null>;
  upsertPinBids(pin: string, bid: string | null, overbid: string | null): Promise<PinBid>;
}

type DbInstance = NodePgDatabase<typeof schema>;

function upsertCache(dbConn: DbInstance, pin: string, source: string, data: unknown, error?: string) {
  return dbConn
    .insert(propertyCacheTable)
    .values({
      pin,
      source,
      data,
      error: error || null,
      fetchedAt: new Date(),
    })
    .onConflictDoUpdate({
      target: [propertyCacheTable.pin, propertyCacheTable.source],
      set: {
        data,
        error: error || null,
        fetchedAt: new Date(),
      },
    });
}

export class DatabaseStorage implements IStorage {
  async getCachedData(pin: string, source: DataSource): Promise<PropertyCache | null> {
    const rows = await db
      .select()
      .from(propertyCacheTable)
      .where(and(eq(propertyCacheTable.pin, pin), eq(propertyCacheTable.source, source)))
      .orderBy(desc(propertyCacheTable.fetchedAt))
      .limit(1);
    return rows[0] || null;
  }

  async setCachedData(pin: string, source: DataSource, data: unknown, error?: string): Promise<void> {
    await upsertCache(db, pin, source, data, error);
  }

  async setCachedDataImport(pin: string, source: DataSource, data: unknown, error?: string): Promise<void> {
    await upsertCache(importDb, pin, source, data, error);
  }

  async getAllCachedDataForPin(pin: string): Promise<Record<DataSource, PropertyCache | null>> {
    const rows = await db
      .select()
      .from(propertyCacheTable)
      .where(eq(propertyCacheTable.pin, pin));

    const result: Record<DataSource, PropertyCache | null> = {
      "tax-portal": null,
      "clerk": null,
      "recorder": null,
      "cookviewer": null,
    };

    for (const row of rows) {
      const source = row.source as DataSource;
      if (SOURCES.includes(source)) {
        if (!result[source] || (row.fetchedAt > result[source]!.fetchedAt)) {
          result[source] = row;
        }
      }
    }

    return result;
  }

  async createImportJob(filename: string, pins: string[]): Promise<ImportJob> {
    const [job] = await importDb
      .insert(importJobsTable)
      .values({
        filename,
        status: "pending",
        totalPins: pins.length,
        completedPins: 0,
        failedPins: 0,
      })
      .returning();

    if (pins.length > 0) {
      const pinValues = pins.map((pin) => ({
        jobId: job.id,
        pin,
        status: "pending" as const,
      }));

      const BATCH_SIZE = 500;
      for (let i = 0; i < pinValues.length; i += BATCH_SIZE) {
        await importDb.insert(importPinsTable).values(pinValues.slice(i, i + BATCH_SIZE));
      }
    }

    return job;
  }

  async getImportJob(jobId: string): Promise<ImportJob | null> {
    const rows = await db
      .select()
      .from(importJobsTable)
      .where(eq(importJobsTable.id, jobId))
      .limit(1);
    return rows[0] || null;
  }

  async getImportJobs(): Promise<ImportJob[]> {
    return db
      .select()
      .from(importJobsTable)
      .orderBy(desc(importJobsTable.createdAt));
  }

  async getPendingPinsForJob(jobId: string, limit: number): Promise<ImportPin[]> {
    return importDb
      .select()
      .from(importPinsTable)
      .where(and(eq(importPinsTable.jobId, jobId), eq(importPinsTable.status, "pending")))
      .orderBy(importPinsTable.id)
      .limit(limit);
  }

  async updateImportPinStatus(pinId: string, status: string, error?: string): Promise<void> {
    await importDb
      .update(importPinsTable)
      .set({
        status,
        error: error || null,
        fetchedAt: status === "complete" || status === "error" ? new Date() : null,
      })
      .where(eq(importPinsTable.id, pinId));
  }

  async updateImportJobCounts(jobId: string): Promise<void> {
    const counts = await importDb
      .select({
        status: importPinsTable.status,
        cnt: count(),
      })
      .from(importPinsTable)
      .where(eq(importPinsTable.jobId, jobId))
      .groupBy(importPinsTable.status);

    const completed = Number(counts.find((c) => c.status === "complete")?.cnt ?? 0);
    const failed = Number(counts.find((c) => c.status === "error")?.cnt ?? 0);

    await importDb
      .update(importJobsTable)
      .set({
        completedPins: completed,
        failedPins: failed,
        updatedAt: new Date(),
      })
      .where(eq(importJobsTable.id, jobId));
  }

  async updateImportJobStatus(jobId: string, status: string): Promise<void> {
    await importDb
      .update(importJobsTable)
      .set({ status, updatedAt: new Date() })
      .where(eq(importJobsTable.id, jobId));
  }

  async getImportJobPins(jobId: string, limit = 100, offset = 0): Promise<ImportPin[]> {
    return db
      .select()
      .from(importPinsTable)
      .where(eq(importPinsTable.jobId, jobId))
      .orderBy(importPinsTable.id)
      .limit(limit)
      .offset(offset);
  }

  async getImportJobPinCounts(jobId: string): Promise<{ total: number; pending: number; fetching: number; complete: number; error: number }> {
    const counts = await db
      .select({
        status: importPinsTable.status,
        cnt: count(),
      })
      .from(importPinsTable)
      .where(eq(importPinsTable.jobId, jobId))
      .groupBy(importPinsTable.status);

    const get = (s: string) => Number(counts.find((c) => c.status === s)?.cnt ?? 0);
    return {
      total: counts.reduce((acc, c) => acc + Number(c.cnt), 0),
      pending: get("pending"),
      fetching: get("fetching"),
      complete: get("complete"),
      error: get("error"),
    };
  }

  async getCacheStats(): Promise<{ totalCachedPins: number; totalCacheRows: number; oldestEntry: Date | null; newestEntry: Date | null }> {
    const result = await db.execute(sql`
      SELECT 
        COUNT(DISTINCT pin) as unique_pins,
        COUNT(*) as total_rows,
        MIN(fetched_at) as oldest,
        MAX(fetched_at) as newest
      FROM property_cache 
      WHERE data IS NOT NULL
    `);
    const row = result.rows[0] as any;
    return {
      totalCachedPins: Number(row?.unique_pins ?? 0),
      totalCacheRows: Number(row?.total_rows ?? 0),
      oldestEntry: row?.oldest ? new Date(row.oldest) : null,
      newestEntry: row?.newest ? new Date(row.newest) : null,
    };
  }
  async getImportedPins(): Promise<string[]> {
    const result = await importDb.execute(sql`
      SELECT DISTINCT pin FROM import_pins ORDER BY pin
    `);
    return result.rows.map((row: any) => row.pin as string);
  }

  async getPinBids(pin: string): Promise<PinBid | null> {
    const rows = await db
      .select()
      .from(pinBidsTable)
      .where(eq(pinBidsTable.pin, pin))
      .limit(1);
    return rows[0] || null;
  }

  async upsertPinBids(pin: string, bid: string | null, overbid: string | null): Promise<PinBid> {
    const [result] = await db
      .insert(pinBidsTable)
      .values({ pin, bid, overbid, updatedAt: new Date() })
      .onConflictDoUpdate({
        target: pinBidsTable.pin,
        set: { bid, overbid, updatedAt: new Date() },
      })
      .returning();
    return result;
  }
}

export const storage = new DatabaseStorage();
