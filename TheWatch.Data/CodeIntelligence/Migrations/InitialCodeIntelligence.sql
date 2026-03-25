-- =============================================================================
-- InitialCodeIntelligence.sql — SQL Server graph tables for code intelligence
-- =============================================================================
-- Creates NODE and EDGE tables using SQL Server 2017+ graph extensions.
-- Requires SQL Server 2017 or later (tested with SQL Server 2025).
--
-- Graph table concepts:
--   NODE tables have a hidden $node_id column (auto-generated JSON identifier).
--   EDGE tables have hidden $from_id and $to_id columns for MATCH traversal.
--   MATCH(n1-(e)->n2) is the graph traversal syntax.
--
-- This script is idempotent — safe to run multiple times.
--
-- Example execution:
--   sqlcmd -S localhost -d TheWatch -i InitialCodeIntelligence.sql
--   -- or from EF Core:
--   await db.Database.ExecuteSqlRawAsync(File.ReadAllText("InitialCodeIntelligence.sql"));
--
-- WAL: Each CREATE is wrapped in IF NOT EXISTS to support incremental migration.
--      Indexes use IF NOT EXISTS (via unique naming) to avoid duplicate index errors.
-- =============================================================================

-- ╔══════════════════════════════════════════════════════════════════════════╗
-- ║ NODE TABLES                                                             ║
-- ╚══════════════════════════════════════════════════════════════════════════╝

-- ── symbols NODE ──────────────────────────────────────────────────────────
-- Each row is a code symbol: class, interface, method, enum, struct, etc.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'symbols' AND is_node = 1)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'symbols' AND is_node = 0)
    BEGIN
        -- Table exists but is not a graph node — drop and recreate
        DROP TABLE IF EXISTS [dbo].[tags];
        DROP TABLE IF EXISTS [dbo].[references];
        DROP TABLE IF EXISTS [dbo].[symbols];
    END

    CREATE TABLE [dbo].[symbols] (
        [Id]           UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [Repo]         NVARCHAR(256)    NOT NULL,
        [Project]      NVARCHAR(256)    NOT NULL,
        [File]         NVARCHAR(1024)   NOT NULL,
        [Kind]         NVARCHAR(64)     NOT NULL,
        [Language]     NVARCHAR(64)     NOT NULL,
        [FullName]     NVARCHAR(1024)   NOT NULL,
        [Signature]    NVARCHAR(2048)   NOT NULL,
        [Lines]        INT              NOT NULL DEFAULT 0,
        [BodyHash]     NVARCHAR(64)     NULL,
        [IndexedAt]    DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_symbols] PRIMARY KEY ([Id])
    ) AS NODE;
END
GO

-- ── documents NODE ────────────────────────────────────────────────────────
-- Each row is a source file (document) in the indexed codebase.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'documents' AND is_node = 1)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'documents' AND is_node = 0)
        DROP TABLE [dbo].[documents];

    CREATE TABLE [dbo].[documents] (
        [Id]           UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [Repo]         NVARCHAR(256)    NOT NULL,
        [Project]      NVARCHAR(256)    NOT NULL,
        [FilePath]     NVARCHAR(1024)   NOT NULL,
        [Language]     NVARCHAR(64)     NOT NULL,
        [Lines]        INT              NOT NULL DEFAULT 0,
        [IndexedAt]    DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_documents] PRIMARY KEY ([Id])
    ) AS NODE;
END
GO

-- ╔══════════════════════════════════════════════════════════════════════════╗
-- ║ EDGE TABLES                                                             ║
-- ╚══════════════════════════════════════════════════════════════════════════╝

-- ── references EDGE ───────────────────────────────────────────────────────
-- Directed relationship between two symbols: Calls, Implements, Extends, etc.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'references' AND is_edge = 1)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'references' AND is_edge = 0)
        DROP TABLE [dbo].[references];

    CREATE TABLE [dbo].[references] (
        [Id]           UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [SourceId]     UNIQUEIDENTIFIER NOT NULL,
        [TargetId]     UNIQUEIDENTIFIER NOT NULL,
        [Kind]         NVARCHAR(64)     NOT NULL,
        [SourceFile]   NVARCHAR(1024)   NULL,
        [SourceLine]   INT              NULL,
        CONSTRAINT [PK_references] PRIMARY KEY ([Id])
    ) AS EDGE;
END
GO

-- ── tags EDGE ─────────────────────────────────────────────────────────────
-- Associates a symbol with a classification tag (#emergency, #port, etc.).
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tags' AND is_edge = 1)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tags' AND is_edge = 0)
        DROP TABLE [dbo].[tags];

    CREATE TABLE [dbo].[tags] (
        [Id]           UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [SymbolId]     UNIQUEIDENTIFIER NOT NULL,
        [Tag]          NVARCHAR(128)    NOT NULL,
        CONSTRAINT [PK_tags] PRIMARY KEY ([Id])
    ) AS EDGE;
END
GO

-- ╔══════════════════════════════════════════════════════════════════════════╗
-- ║ INDEXES                                                                 ║
-- ╚══════════════════════════════════════════════════════════════════════════╝

-- Symbols: query by FullName (method/class lookups)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_symbols_FullName' AND object_id = OBJECT_ID('symbols'))
    CREATE NONCLUSTERED INDEX [IX_symbols_FullName] ON [dbo].[symbols] ([FullName]);
GO

-- Symbols: query by Repo (filter to a single repo)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_symbols_Repo' AND object_id = OBJECT_ID('symbols'))
    CREATE NONCLUSTERED INDEX [IX_symbols_Repo] ON [dbo].[symbols] ([Repo]);
GO

-- Symbols: query by Kind (all interfaces, all classes, etc.)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_symbols_Kind' AND object_id = OBJECT_ID('symbols'))
    CREATE NONCLUSTERED INDEX [IX_symbols_Kind] ON [dbo].[symbols] ([Kind]);
GO

-- Symbols: query by Language (all csharp, all kotlin, etc.)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_symbols_Language' AND object_id = OBJECT_ID('symbols'))
    CREATE NONCLUSTERED INDEX [IX_symbols_Language] ON [dbo].[symbols] ([Language]);
GO

-- Symbols: unique constraint to prevent duplicate symbols on re-index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_symbols_Repo_File_FullName' AND object_id = OBJECT_ID('symbols'))
    CREATE UNIQUE NONCLUSTERED INDEX [UX_symbols_Repo_File_FullName]
        ON [dbo].[symbols] ([Repo], [File], [FullName]);
GO

-- Documents: unique constraint on repo + file path
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_documents_Repo_FilePath' AND object_id = OBJECT_ID('documents'))
    CREATE UNIQUE NONCLUSTERED INDEX [UX_documents_Repo_FilePath]
        ON [dbo].[documents] ([Repo], [FilePath]);
GO

-- References: query by source or target symbol for traversal
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_references_SourceId' AND object_id = OBJECT_ID('references'))
    CREATE NONCLUSTERED INDEX [IX_references_SourceId] ON [dbo].[references] ([SourceId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_references_TargetId' AND object_id = OBJECT_ID('references'))
    CREATE NONCLUSTERED INDEX [IX_references_TargetId] ON [dbo].[references] ([TargetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_references_Kind' AND object_id = OBJECT_ID('references'))
    CREATE NONCLUSTERED INDEX [IX_references_Kind] ON [dbo].[references] ([Kind]);
GO

-- Tags: query by tag value (e.g., find all #emergency symbols)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tags_Tag' AND object_id = OBJECT_ID('tags'))
    CREATE NONCLUSTERED INDEX [IX_tags_Tag] ON [dbo].[tags] ([Tag]);
GO

-- Tags: query by symbol to get all tags for a symbol
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tags_SymbolId' AND object_id = OBJECT_ID('tags'))
    CREATE NONCLUSTERED INDEX [IX_tags_SymbolId] ON [dbo].[tags] ([SymbolId]);
GO

-- ╔══════════════════════════════════════════════════════════════════════════╗
-- ║ GRAPH EDGE CONSTRAINTS (SQL Server 2019+)                               ║
-- ╚══════════════════════════════════════════════════════════════════════════╝
-- Edge constraints ensure $from_id and $to_id point to valid node tables.
-- These are optional but improve query optimizer decisions.

IF NOT EXISTS (SELECT 1 FROM sys.edge_constraints WHERE name = 'EC_references_symbols_to_symbols')
BEGIN
    ALTER TABLE [dbo].[references]
        ADD CONSTRAINT [EC_references_symbols_to_symbols]
        CONNECTION (symbols TO symbols, symbols TO documents, documents TO symbols);
END
GO

PRINT 'Code intelligence graph tables created successfully.';
GO
