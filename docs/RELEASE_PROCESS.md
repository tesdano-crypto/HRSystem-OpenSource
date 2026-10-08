# Release Process

## 基線與分支

每個階段從已驗證 Stable Tag 建立新分支，不從未確認的 HEAD 開發，不修改或移動舊 Tag，也不重寫歷史。

```powershell
git switch --detach <stable-tag>
git switch -c <new-branch>
```

建立後確認 Branch、HEAD、Tag Target 與 Working Tree clean。

## 驗證順序

1. `scripts\verify-environment.ps1`
2. `scripts\build.ps1`
3. `scripts\test.ps1 -NoBuild`
4. `scripts\check-migrations.ps1`
5. 人工檢查 Migration Up／Down 與資料影響
6. 僅在允許的 Development Database 進行實際驗證
7. Commit 後於 clean Working Tree 執行 `scripts\release-audit.ps1`
8. 交叉執行原始 dotnet clean／restore／build／test／EF 指令
9. 建立 Release Tag 並確認 Tag 指向最終 Commit

Release Audit 腳本失敗時立即停止；腳本不會自動 Push，也不能取代 Migration、Security 或資料影響的人工審查。

## Commit、Tag 與 Rollback

Commit Message 與 Tag 必須使用該階段指定值。建立 Tag 前 Working Tree 必須 clean，測試與 Migration Check 必須通過。不得 Force Push；除非使用者另行授權，本機 Release 不 Push。

Rollback Reference 是前一個 Stable Tag。資料庫變更另需依已人工審查的 Down 或專門回復計畫處理，不能以 Git 回退取代資料庫安全程序。

## Development Verification

所有 SQL 驗證先通過 Server、Database 與 Windows Integrated 安全閘門。Migration Check 是唯讀；套用 Migration 必須由階段需求明確授權。Development 驗證不得使用公司資料庫或 SQL Login。

## 環境失敗與功能失敗

- 環境失敗：SDK 缺少、NuGet 無法存取、DPAPI 使用者不一致、Application Control、磁碟或權限問題。先停止功能開發並蒐集環境證據。
- 功能失敗：可在相同可信環境穩定重現的 Build Error、測試斷言或產品行為錯誤。依功能範圍修正並重新完整驗證。

不得把環境錯誤誤判為產品缺陷，也不得以 Skip Test、管理員權限或關閉安全功能讓 Release 通過。
