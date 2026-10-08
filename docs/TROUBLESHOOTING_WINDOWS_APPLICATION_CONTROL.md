# Windows Application Control Troubleshooting

## Symptoms

Windows Application Control may reject a test DLL with FileLoadException.
Collect the actual HRESULT and the corresponding Code Integrity event before
changing anything. Do not disable security policy to make a build pass.

## 找出被封鎖 DLL

保留例外中的完整 DLL 路徑、HRESULT、失敗 Test Project 與時間。只針對該檔案讀取大小、時間、擁有者、ACL 與 SHA-256：

```powershell
Get-Item '<blocked-dll>'
Get-FileHash '<blocked-dll>' -Algorithm SHA256
Get-Acl '<blocked-dll>'
```

不得查詢或輸出 User Secrets、密碼、Token、PasswordHash、SecurityStamp 或 Connection String。

## Mark of the Web

```powershell
Get-Item '<blocked-dll>' -Stream *
Get-Content '<blocked-dll>' -Stream Zone.Identifier
```

若只有 `:$DATA`，目前證據不支持 MOTW。不要對整個 Repo、磁碟或使用者目錄批次執行 `Unblock-File`。只有明確確認安全來源、MOTW 為根因且範圍最小時，才交由操作者評估單檔處理。

## Code Integrity 與 AppLocker

以一般使用者唯讀查詢與失敗時間相符的必要事件：

```powershell
Get-WinEvent -LogName 'Microsoft-Windows-CodeIntegrity/Operational' -MaxEvents 50
Get-WinEvent -LogName 'Microsoft-Windows-AppLocker/EXE and DLL' -MaxEvents 50
Get-WinEvent -LogName 'Microsoft-Windows-AppLocker/MSI and Script' -MaxEvents 50
```

只保留 TimeCreated、Provider、Event ID、Policy、檔案、Status 與簽章摘要。若存取被拒，記錄限制，不提升權限繞過。

## 安全重現順序

1. 執行 `scripts\verify-environment.ps1`。
2. `dotnet clean`，只刪除專案 `src/tests` 下的 bin、obj、TestResults。
3. 重新 Restore、Build、Test。
4. 若仍失敗，以 `git worktree add --detach <local-short-path> <stable-tag>` 建立乾淨 worktree；不複製 Secrets、資料庫或產生物。
5. 比較原路徑與 worktree 的雜湊、Owner、ADS 與新事件。
6. 可用命令列 `--artifacts-path .artifacts` 做暫時輸出隔離，完成後清除。

安全方式包含清理專案產生物、由實際開發使用者重建、使用乾淨本機 worktree、修正專案自己的路徑或 ACL，以及固定正確 SDK。

## 禁止方式

不得停用 Defender、WDAC、Smart App Control 或 AppLocker；不得修改群組原則、Registry 安全設定、PowerShell Execution Policy、Defender 排除或即時防護；不得 Skip／刪除測試、替換 DLL、重新簽章、複製到系統目錄或以系統管理員權限作為日常解法。

## 交由系統管理員的條件

若乾淨產物、乾淨本機 worktree、正確 SDK 與一般使用者仍穩定重現，提供：事件來源／ID、Policy 名稱或 ID、被封鎖路徑、SHA-256、簽章狀態、重現命令、HRESULT、兩種路徑結果與已排除項目。請管理員評估允許的開發者工作負載政策，不要求關閉安全功能。
