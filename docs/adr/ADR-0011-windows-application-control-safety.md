# ADR-0011：Windows Application Control Safety

Status: Accepted

## Context

測試產物曾被 Windows Code Integrity 以 `0x800711C7` 阻擋。直接關閉安全功能會破壞企業環境的信任基線。

## Decision

尊重 WDAC、Smart App Control、AppLocker 與 Defender。優先檢查來源、MOTW、ACL、路徑、產物擁有者與事件證據，再以一般使用者清理並重建產物或使用乾淨 worktree。需要政策評估時交由系統管理員，不自行修改政策。

## Consequences

診斷可能需要額外時間，但不依賴安全排除或管理員繞過。事件證據只保留必要欄位，不提交原始事件匯出或本機隱私資訊。
