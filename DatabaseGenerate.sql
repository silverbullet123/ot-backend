/* DatabaseGenerate.sql
   Recreates OT_Assessment_DB schema, tables, indexes, and sprocs.
   Target: SQL Server 2022 Developer Edition
*/
SET NOCOUNT ON;
IF DB_ID('OT_Assessment_DB') IS NULL
BEGIN
    PRINT('Creating database OT_Assessment_DB...');
    CREATE DATABASE OT_Assessment_DB;
END
GO
USE OT_Assessment_DB;
GO

-- Drop existing objects (idempotent)
IF OBJECT_ID('dbo.Wager', 'U') IS NOT NULL DROP TABLE dbo.Wager;
IF OBJECT_ID('dbo.Game', 'U') IS NOT NULL DROP TABLE dbo.Game;
IF OBJECT_ID('dbo.Provider', 'U') IS NOT NULL DROP TABLE dbo.Provider;
IF OBJECT_ID('dbo.Brand', 'U') IS NOT NULL DROP TABLE dbo.Brand;
IF OBJECT_ID('dbo.Player', 'U') IS NOT NULL DROP TABLE dbo.Player;
GO

CREATE TABLE dbo.Player (
    AccountId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Username NVARCHAR(200) NOT NULL,
    CountryCode CHAR(2) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Player_CreatedAt DEFAULT (SYSUTCDATETIME())
);
GO

CREATE TABLE dbo.Provider (
    ProviderId INT IDENTITY(1,1) PRIMARY KEY,
    ProviderName NVARCHAR(200) NOT NULL UNIQUE
);
GO

CREATE TABLE dbo.Game (
    GameId INT IDENTITY(1,1) PRIMARY KEY,
    GameName NVARCHAR(200) NOT NULL,
    Theme NVARCHAR(100) NULL,
    ProviderId INT NOT NULL,
    CONSTRAINT UQ_Game UNIQUE (GameName, ProviderId),
    CONSTRAINT FK_Game_Provider FOREIGN KEY (ProviderId) REFERENCES dbo.Provider(ProviderId)
);
GO

CREATE TABLE dbo.Brand (
    BrandId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
);
GO

CREATE TABLE dbo.Wager (
    WagerId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    TransactionId UNIQUEIDENTIFIER NOT NULL UNIQUE,
    AccountId UNIQUEIDENTIFIER NOT NULL,
    BrandId UNIQUEIDENTIFIER NOT NULL,
    GameId INT NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    NumberOfBets INT NOT NULL,
    DurationMs BIGINT NULL,
    SessionData NVARCHAR(4000) NULL,
    CreatedDateTime DATETIME2(3) NOT NULL,
    IngestedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Wager_IngestedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Wager_Player FOREIGN KEY (AccountId) REFERENCES dbo.Player(AccountId),
    CONSTRAINT FK_Wager_Brand FOREIGN KEY (BrandId) REFERENCES dbo.Brand(BrandId),
    CONSTRAINT FK_Wager_Game FOREIGN KEY (GameId) REFERENCES dbo.Game(GameId)
);
GO

-- Helpful indexes
CREATE INDEX IX_Wager_AccountId_Created ON dbo.Wager(AccountId, CreatedDateTime DESC) INCLUDE (Amount, GameId);
CREATE INDEX IX_Wager_Created ON dbo.Wager(CreatedDateTime DESC);
GO

-- Upsert sprocs (Provider, Game, Player, Brand)
IF OBJECT_ID('dbo.UpsertProvider', 'P') IS NOT NULL DROP PROCEDURE dbo.UpsertProvider;
GO
CREATE PROCEDURE dbo.UpsertProvider @ProviderName NVARCHAR(200), @ProviderId INT OUTPUT AS
BEGIN
    SET NOCOUNT ON;
    SELECT @ProviderId = ProviderId FROM dbo.Provider WITH (UPDLOCK, HOLDLOCK) WHERE ProviderName = @ProviderName;
    IF @ProviderId IS NULL
    BEGIN
        INSERT INTO dbo.Provider (ProviderName) VALUES (@ProviderName);
        SET @ProviderId = SCOPE_IDENTITY();
    END
END
GO

IF OBJECT_ID('dbo.UpsertGame', 'P') IS NOT NULL DROP PROCEDURE dbo.UpsertGame;
GO
CREATE PROCEDURE dbo.UpsertGame
    @GameName NVARCHAR(200),
    @Theme NVARCHAR(100),
    @ProviderName NVARCHAR(200),
    @GameId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ProviderId INT;
    EXEC dbo.UpsertProvider @ProviderName, @ProviderId OUTPUT;

    SELECT @GameId = GameId FROM dbo.Game WITH (UPDLOCK, HOLDLOCK)
    WHERE GameName = @GameName AND ProviderId = @ProviderId;

    IF @GameId IS NULL
    BEGIN
        INSERT INTO dbo.Game (GameName, Theme, ProviderId)
        VALUES (@GameName, @Theme, @ProviderId);
        SET @GameId = SCOPE_IDENTITY();
    END
END
GO

IF OBJECT_ID('dbo.UpsertPlayer', 'P') IS NOT NULL DROP PROCEDURE dbo.UpsertPlayer;
GO
CREATE PROCEDURE dbo.UpsertPlayer
    @AccountId UNIQUEIDENTIFIER,
    @Username NVARCHAR(200),
    @CountryCode CHAR(2) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Player WITH (UPDLOCK, HOLDLOCK) WHERE AccountId = @AccountId)
    BEGIN
        INSERT INTO dbo.Player (AccountId, Username, CountryCode) VALUES (@AccountId, @Username, @CountryCode);
    END
    ELSE
    BEGIN
        UPDATE dbo.Player SET Username = @Username, CountryCode = @CountryCode WHERE AccountId = @AccountId;
    END
END
GO

IF OBJECT_ID('dbo.UpsertBrand', 'P') IS NOT NULL DROP PROCEDURE dbo.UpsertBrand;
GO
CREATE PROCEDURE dbo.UpsertBrand @BrandId UNIQUEIDENTIFIER AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Brand WITH (UPDLOCK, HOLDLOCK) WHERE BrandId = @BrandId)
        INSERT INTO dbo.Brand (BrandId) VALUES (@BrandId);
END
GO

-- Insert Wager sproc (idempotent on TransactionId)
IF OBJECT_ID('dbo.InsertWager', 'P') IS NOT NULL DROP PROCEDURE dbo.InsertWager;
GO
CREATE PROCEDURE dbo.InsertWager
    @WagerId UNIQUEIDENTIFIER,
    @TransactionId UNIQUEIDENTIFIER,
    @AccountId UNIQUEIDENTIFIER,
    @Username NVARCHAR(200),
    @CountryCode CHAR(2),
    @BrandId UNIQUEIDENTIFIER,
    @GameName NVARCHAR(200),
    @Theme NVARCHAR(100),
    @ProviderName NVARCHAR(200),
    @Amount DECIMAL(18,2),
    @NumberOfBets INT,
    @DurationMs BIGINT,
    @SessionData NVARCHAR(4000),
    @CreatedDateTime DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    -- Ensure dimensions exist
    EXEC dbo.UpsertPlayer @AccountId, @Username, @CountryCode;
    EXEC dbo.UpsertBrand @BrandId;
    DECLARE @GameId INT;
    EXEC dbo.UpsertGame @GameName, @Theme, @ProviderName, @GameId OUTPUT;

    -- Idempotent insert
    IF NOT EXISTS (SELECT 1 FROM dbo.Wager WITH (UPDLOCK, HOLDLOCK) WHERE TransactionId = @TransactionId)
    BEGIN
        INSERT INTO dbo.Wager (WagerId, TransactionId, AccountId, BrandId, GameId, Amount, NumberOfBets, DurationMs, SessionData, CreatedDateTime)
        VALUES (@WagerId, @TransactionId, @AccountId, @BrandId, @GameId, @Amount, @NumberOfBets, @DurationMs, @SessionData, @CreatedDateTime);
    END
END
GO

-- Query: Paged wagers by player
IF OBJECT_ID('dbo.GetPlayerCasinoWagersPaged', 'P') IS NOT NULL DROP PROCEDURE dbo.GetPlayerCasinoWagersPaged;
GO
CREATE PROCEDURE dbo.GetPlayerCasinoWagersPaged
    @AccountId UNIQUEIDENTIFIER,
    @Page INT = 1,
    @PageSize INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @PageSize;

    ;WITH W AS (
        SELECT w.WagerId, g.GameName AS Game, p.ProviderName AS Provider, w.Amount, w.CreatedDateTime,
               ROW_NUMBER() OVER (ORDER BY w.CreatedDateTime DESC, w.WagerId DESC) AS rn
        FROM dbo.Wager w
        INNER JOIN dbo.Game g ON w.GameId = g.GameId
        INNER JOIN dbo.Provider p ON g.ProviderId = p.ProviderId
        WHERE w.AccountId = @AccountId
    )
    SELECT WagerId, Game, Provider, Amount, CreatedDateTime
    FROM W WHERE rn BETWEEN @Offset + 1 AND @Offset + @PageSize;

    SELECT COUNT(*) AS Total FROM dbo.Wager WHERE AccountId = @AccountId;
END
GO

-- Query: Top spenders
IF OBJECT_ID('dbo.GetTopSpenders', 'P') IS NOT NULL DROP PROCEDURE dbo.GetTopSpenders;
GO
CREATE PROCEDURE dbo.GetTopSpenders
    @Count INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Count)
        w.AccountId,
        pl.Username,
        SUM(w.Amount) AS TotalAmountSpend
    FROM dbo.Wager w
    INNER JOIN dbo.Player pl ON pl.AccountId = w.AccountId
    GROUP BY w.AccountId, pl.Username
    ORDER BY SUM(w.Amount) DESC;
END
GO
