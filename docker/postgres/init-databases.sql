-- Runs once, the first time the PostgreSQL volume is created.
-- Each service owns its own database (database-per-service).
CREATE DATABASE rides;
CREATE DATABASE drivers;
CREATE DATABASE analytics;
