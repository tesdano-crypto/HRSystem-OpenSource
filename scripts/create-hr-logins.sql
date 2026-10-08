:setvar DatabaseName "HRSystemDB"
:setvar AppLogin "hrsystem_runtime"
:setvar AppPassword "REPLACE_BEFORE_EXECUTION"
:setvar MigrationLogin "hrsystem_migration"
:setvar MigrationPassword "REPLACE_BEFORE_EXECUTION"

/*
  由 DBA 以 SQLCMD 模式執行。請在安全管道提供兩組不同的強密碼。
  AppLogin 僅供執行期 DML；MigrationLogin 僅在部署 Migration 時暫用。
  不授與 sysadmin、securityadmin 或 db_owner。
*/
SET NOCOUNT ON;

IF N'$(AppPassword)' = N'REPLACE_BEFORE_EXECUTION'
   OR N'$(MigrationPassword)' = N'REPLACE_BEFORE_EXECUTION'
    THROW 50001, '請先透過安全方式設定 AppPassword 與 MigrationPassword SQLCMD 變數。', 1;

IF SUSER_ID(N'$(AppLogin)') IS NULL
BEGIN
    DECLARE @createAppLogin nvarchar(max) =
        N'CREATE LOGIN ' + QUOTENAME(N'$(AppLogin)') +
        N' WITH PASSWORD = ' + QUOTENAME(N'$(AppPassword)', '''') +
        N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
    EXEC sys.sp_executesql @createAppLogin;
END;

IF SUSER_ID(N'$(MigrationLogin)') IS NULL
BEGIN
    DECLARE @createMigrationLogin nvarchar(max) =
        N'CREATE LOGIN ' + QUOTENAME(N'$(MigrationLogin)') +
        N' WITH PASSWORD = ' + QUOTENAME(N'$(MigrationPassword)', '''') +
        N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
    EXEC sys.sp_executesql @createMigrationLogin;
END;
GO

USE [$(DatabaseName)];
GO

IF USER_ID(N'$(AppLogin)') IS NULL
    EXEC(N'CREATE USER ' + QUOTENAME(N'$(AppLogin)') + N' FOR LOGIN ' + QUOTENAME(N'$(AppLogin)') + N';');
IF USER_ID(N'$(MigrationLogin)') IS NULL
    EXEC(N'CREATE USER ' + QUOTENAME(N'$(MigrationLogin)') + N' FOR LOGIN ' + QUOTENAME(N'$(MigrationLogin)') + N';');
GO

IF IS_ROLEMEMBER(N'db_datareader', N'$(AppLogin)') <> 1
    ALTER ROLE [db_datareader] ADD MEMBER [$(AppLogin)];
IF IS_ROLEMEMBER(N'db_datawriter', N'$(AppLogin)') <> 1
    ALTER ROLE [db_datawriter] ADD MEMBER [$(AppLogin)];

IF IS_ROLEMEMBER(N'db_ddladmin', N'$(MigrationLogin)') <> 1
    ALTER ROLE [db_ddladmin] ADD MEMBER [$(MigrationLogin)];
IF IS_ROLEMEMBER(N'db_datareader', N'$(MigrationLogin)') <> 1
    ALTER ROLE [db_datareader] ADD MEMBER [$(MigrationLogin)];
IF IS_ROLEMEMBER(N'db_datawriter', N'$(MigrationLogin)') <> 1
    ALTER ROLE [db_datawriter] ADD MEMBER [$(MigrationLogin)];
GO
