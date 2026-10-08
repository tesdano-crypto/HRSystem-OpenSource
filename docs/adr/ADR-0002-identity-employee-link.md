# ADR-0002：Identity 與 Employee 連結

Status: Accepted

## Context

登入帳號生命週期與員工主資料生命週期並不完全相同。

## Decision

ApplicationUser 與 Employee 分離，以可空 EmployeeId 建立連結；角色與登入狀態屬於 Identity，組織資料屬於 Employee。

## Consequences

Admin 可不綁 Employee；需要本人或部門範圍的功能必須安全處理未綁定與停用員工情境。
