# ADR-0001：Modular Monolith

Status: Accepted

## Context

內網 HR 系統需要清楚分層，但目前規模不需要分散式部署。

## Decision

採單一 Solution 的 Modular Monolith，分為 Domain、Application、Infrastructure、Web 與測試專案，依賴方向朝向 Domain/Application。

## Consequences

部署與交易邊界維持簡單；模組必須遵守分層，不可由 UI 繞過 Application 規則。
