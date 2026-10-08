# ADR-0010：Scripted Release Audit

Status: Accepted

## Context

手動重複 Git、Build、Test、Migration 與安全掃描容易遺漏或產生不一致結果。

## Decision

以 `scripts/release-audit.ps1` 按固定順序執行 Release Gate；任何真正 FAIL 都停止 Release。腳本不建立 Migration、不套用 Migration、不修改資料庫、不 Push，也不取代 Migration Up/Down 的人工審查。

## Consequences

Release 流程可重複並提供一致摘要；合理文件字串與測試資料仍需人工判斷，不能把關鍵字掃描當成全部安全審查。
