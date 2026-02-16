-- Cook County Property System - PostgreSQL Schema
-- Run this SQL to create all required tables, or use Drizzle ORM: npm run db:push

-- Property data cache (4 sources per PIN)
CREATE TABLE IF NOT EXISTS property_cache (
  id VARCHAR PRIMARY KEY DEFAULT gen_random_uuid(),
  pin VARCHAR NOT NULL,
  source VARCHAR NOT NULL,           -- 'tax-portal' | 'clerk' | 'recorder' | 'cookviewer'
  data JSONB,                        -- Cached API response data
  fetched_at TIMESTAMP NOT NULL DEFAULT now(),
  error TEXT                         -- Error message if fetch failed
);

-- Bid/Overbid values per PIN
CREATE TABLE IF NOT EXISTS pin_bids (
  pin VARCHAR PRIMARY KEY,
  bid TEXT,                          -- Bid amount (decimal string)
  overbid TEXT,                      -- Overbid amount (decimal string)
  updated_at TIMESTAMP NOT NULL DEFAULT now()
);

-- Bulk import jobs
CREATE TABLE IF NOT EXISTS import_jobs (
  id VARCHAR PRIMARY KEY DEFAULT gen_random_uuid(),
  filename TEXT NOT NULL,
  status VARCHAR NOT NULL DEFAULT 'pending',   -- 'pending' | 'processing' | 'completed' | 'failed'
  total_pins INTEGER NOT NULL DEFAULT 0,
  completed_pins INTEGER NOT NULL DEFAULT 0,
  failed_pins INTEGER NOT NULL DEFAULT 0,
  created_at TIMESTAMP NOT NULL DEFAULT now(),
  updated_at TIMESTAMP NOT NULL DEFAULT now()
);

-- Individual PINs within an import job
CREATE TABLE IF NOT EXISTS import_pins (
  id VARCHAR PRIMARY KEY DEFAULT gen_random_uuid(),
  job_id VARCHAR NOT NULL,
  pin VARCHAR NOT NULL,
  status VARCHAR NOT NULL DEFAULT 'pending',   -- 'pending' | 'processing' | 'completed' | 'failed'
  error TEXT,
  fetched_at TIMESTAMP
);

-- Users (for session auth, if enabled)
CREATE TABLE IF NOT EXISTS users (
  id VARCHAR PRIMARY KEY DEFAULT gen_random_uuid(),
  username TEXT NOT NULL,
  password TEXT NOT NULL
);
