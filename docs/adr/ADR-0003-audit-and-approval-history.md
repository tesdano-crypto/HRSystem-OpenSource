# ADR-0003：Audit 與 Approval History

Status: Accepted

## Context

帳號管理與請假狀態變更需要不同用途的追蹤紀錄。

## Decision

AuditLog 記錄安全的操作摘要；各既有 workflow history 與共用
`ApprovalHistory` 專門保存 append-only 簽核歷程。狀態、歷程、稽核與一次性
簽核 token 的消耗／撤銷在同一交易邊界完成。外部通知在送簽交易 commit 後執行，
通知失敗不得回滾簽核。

## Consequences

歷程不可修改或刪除；稽核內容不得包含密碼、token、完整 LINE UserId、完整原因、
簽核意見或連線設定。來源指紋與一次性 token hash 用於驗證，不作使用者顯示內容。
