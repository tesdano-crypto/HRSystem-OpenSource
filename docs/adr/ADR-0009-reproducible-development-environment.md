# ADR-0009：Reproducible Development Environment

Status: Accepted

## Context

開發結果若依賴特定 IDE、未固定 SDK 或不同 Windows 使用者產生物，Build/Test 難以重複並可能觸發信任邊界問題。

## Decision

以 `global.json` 固定已驗證的 .NET SDK，並以 Repo 內 PowerShell scripts 統一 Environment、Build、Test、Migration Check 與 Development Run。流程不依賴特定 IDE，且以一般使用者權限完成。

## Consequences

新環境缺少 SDK 時會明確失敗；SDK 更新必須經完整測試。腳本增加維護責任，但提供一致、可稽核且不降低安全性的開發入口。
