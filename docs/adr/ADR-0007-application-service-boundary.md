# ADR-0007：Application Service 邊界

Status: Accepted

## Context

角色授權、資料範圍與商業規則若散落 UI，容易被其他入口繞過。

## Decision

Application Service 負責授權後的資料範圍、流程與查詢投影；Domain 保存核心狀態規則；Razor 只負責 ViewModel、Binding 與 Navigation。

## Consequences

UI 不直接操作 DbContext 或 Entity；服務介面與 DTO 成為可測試的應用邊界。
