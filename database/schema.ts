import { sql } from "drizzle-orm";
import { pgTable, text, varchar, timestamp, integer, jsonb, index, uniqueIndex } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod";

export const users = pgTable("users", {
  id: varchar("id").primaryKey().default(sql`gen_random_uuid()`),
  username: text("username").notNull().unique(),
  password: text("password").notNull(),
});

export const insertUserSchema = createInsertSchema(users).pick({
  username: true,
  password: true,
});

export type InsertUser = z.infer<typeof insertUserSchema>;
export type User = typeof users.$inferSelect;

export const propertyCacheTable = pgTable("property_cache", {
  id: varchar("id").primaryKey().default(sql`gen_random_uuid()`),
  pin: varchar("pin", { length: 20 }).notNull(),
  source: varchar("source", { length: 30 }).notNull(),
  data: jsonb("data"),
  fetchedAt: timestamp("fetched_at").notNull().defaultNow(),
  error: text("error"),
}, (table) => [
  index("idx_property_cache_pin").on(table.pin),
  uniqueIndex("uq_property_cache_pin_source").on(table.pin, table.source),
]);

export type PropertyCache = typeof propertyCacheTable.$inferSelect;
export type InsertPropertyCache = typeof propertyCacheTable.$inferInsert;

export const importJobsTable = pgTable("import_jobs", {
  id: varchar("id").primaryKey().default(sql`gen_random_uuid()`),
  filename: text("filename").notNull(),
  status: varchar("status", { length: 20 }).notNull().default("pending"),
  totalPins: integer("total_pins").notNull().default(0),
  completedPins: integer("completed_pins").notNull().default(0),
  failedPins: integer("failed_pins").notNull().default(0),
  createdAt: timestamp("created_at").notNull().defaultNow(),
  updatedAt: timestamp("updated_at").notNull().defaultNow(),
});

export type ImportJob = typeof importJobsTable.$inferSelect;
export type InsertImportJob = typeof importJobsTable.$inferInsert;

export const importPinsTable = pgTable("import_pins", {
  id: varchar("id").primaryKey().default(sql`gen_random_uuid()`),
  jobId: varchar("job_id").notNull().references(() => importJobsTable.id),
  pin: varchar("pin", { length: 20 }).notNull(),
  status: varchar("status", { length: 20 }).notNull().default("pending"),
  error: text("error"),
  fetchedAt: timestamp("fetched_at"),
}, (table) => [
  index("idx_import_pins_job_id").on(table.jobId),
  index("idx_import_pins_status").on(table.status),
  index("idx_import_pins_job_status").on(table.jobId, table.status),
]);

export type ImportPin = typeof importPinsTable.$inferSelect;
export type InsertImportPin = typeof importPinsTable.$inferInsert;

export const pinBidsTable = pgTable("pin_bids", {
  pin: varchar("pin", { length: 20 }).primaryKey(),
  bid: text("bid"),
  overbid: text("overbid"),
  updatedAt: timestamp("updated_at").notNull().defaultNow(),
});

export type PinBid = typeof pinBidsTable.$inferSelect;
export type InsertPinBid = typeof pinBidsTable.$inferInsert;

// Cook County PIN validation schema
export const pinSchema = z.string().regex(
  /^\d{2}-\d{2}-\d{3}-\d{3}-\d{4}$/,
  "PIN must be in format XX-XX-XXX-XXX-XXXX"
);

// API Response schemas
export const apiErrorSchema = z.object({
  success: z.literal(false),
  error: z.string(),
  code: z.string().optional(),
});

export const taxDelinquentResponseSchema = z.object({
  success: z.literal(true),
  data: z.object({
    html: z.string(),
    pin: z.string(),
    fetchedAt: z.string(),
  }),
});

export const propertyInfoResponseSchema = z.object({
  success: z.literal(true),
  data: z.object({
    html: z.string(),
    pin: z.string(),
    fetchedAt: z.string(),
  }),
});

export const documentSearchResponseSchema = z.object({
  success: z.literal(true),
  data: z.object({
    redirectUrl: z.string(),
    documentId: z.string().optional(),
    historyId: z.string().optional(),
    pin: z.string(),
    fetchedAt: z.string(),
  }),
});

// TypeScript types
export type ApiError = z.infer<typeof apiErrorSchema>;
export type TaxDelinquentResponse = z.infer<typeof taxDelinquentResponseSchema>;
export type PropertyInfoResponse = z.infer<typeof propertyInfoResponseSchema>;
export type DocumentSearchResponse = z.infer<typeof documentSearchResponseSchema>;

// Unified API response type
export type ApiResponse<T> = T | ApiError;

// API Endpoint documentation types
export interface ApiEndpoint {
  method: "GET" | "POST";
  path: string;
  description: string;
  parameters: {
    name: string;
    type: string;
    required: boolean;
    description: string;
    example: string;
  }[];
  responses: {
    status: number;
    description: string;
    example: object;
  }[];
}
