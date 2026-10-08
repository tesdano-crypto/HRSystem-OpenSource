:setvar DatabaseName "HRSystemDB"

/*
  由具備 CREATE DATABASE 權限的 DBA 執行。
  執行前請確認 SQLCMD 變數 DatabaseName；本腳本不建立或使用 sa。
*/
SET NOCOUNT ON;

IF DB_ID(N'$(DatabaseName)') IS NULL
BEGIN
    DECLARE @createDatabase nvarchar(max) =
        N'CREATE DATABASE ' + QUOTENAME(N'$(DatabaseName)') + N';';
    EXEC sys.sp_executesql @createDatabase;
END;
GO
