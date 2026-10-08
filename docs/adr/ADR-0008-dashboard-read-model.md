# ADR-0008：Dashboard Read Model

Status: Accepted

## Context

角色 Dashboard 需要快速摘要既有資料，但目前資料量與需求不需要額外同步機制。

## Decision

Dashboard 使用既有資料即時計算只讀摘要，不建立 Dashboard Summary Table，不建立快取資料庫，不產生瀏覽 AuditLog，且不在 UI 直接查 DbContext。Application Service 以角色與 Employee 綁定限制資料範圍。

## Consequences

資料保持即時且沒有同步負擔；查詢必須使用投影、聚合與筆數上限。未來若效能需求改變，需另立 ADR，不在本階段預先加入快取或背景工作。
