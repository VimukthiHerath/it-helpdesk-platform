-- One-time baseline for a Ticket database that was created by EnsureCreated().
--
-- Until now the Ticket service built its tables at start-up with EnsureCreated(), which leaves no
-- __EFMigrationsHistory table. CI now applies migrations with `dotnet ef database update`, and on such a
-- database it fails with "Table 'Tickets' already exists".
--
-- This marks the three ApplicationDbContext migrations as already applied, but only when the Tickets table
-- is already there. On a brand new database it does nothing, so `dotnet ef database update` creates everything.
-- Safe to run more than once. Run it once against the real ticket database, then CI works as normal.
--
--   mysql -h <server> -u <user> -p <ticket_db> < services/ticket/baseline-migrations.sql

CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
) CHARACTER SET utf8mb4;

INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
SELECT m.`MigrationId`, '8.0.0'
FROM (
    SELECT '20260823092414_InitialCreate' AS `MigrationId`
    UNION ALL SELECT '20260823095937_TicketAttributes'
    UNION ALL SELECT '20260915125052_AddTicketStatusHistory'
) AS m
WHERE EXISTS (
    SELECT 1 FROM information_schema.tables
    WHERE table_schema = DATABASE() AND table_name = 'Tickets'
);
