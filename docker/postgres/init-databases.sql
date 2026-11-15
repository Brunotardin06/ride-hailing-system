-- Runs only once: the first time the postgres-data volume is created.
-- Database-per-service: each service owns its own database.
CREATE DATABASE rides;
CREATE DATABASE drivers;

