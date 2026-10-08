# ADR-0012：員工編號使用 SQL Server Sequence

- 狀態：Accepted
- 日期：2026-07-25

## Context

新 Employee 必須自動取得固定格式 `EMP0001`–`EMP9999` 的 Employee Number。配號必須支援多程序、多應用執行個體與並行建立，且不可依賴 process-local lock。

原設計在 Serializable transaction 內於 insert 前查詢 generated Employee Number 是否存在。真實 SQL 並行測試證明此查詢會取得 key-range lock，與另一筆 Employee insert 形成 SQL Server 1205 deadlock，即使兩個 request 已由 Sequence 取得不同數值。

## Decision

- 使用 `[dbo].[EmployeeNumberSequence]` 配發 `int` 數值：start/min 15、increment 1、max 9,999、non-cycling。
- Application 透過 `IEmployeeNumberSequence` port 取值，格式化為固定大寫 `EMP####`；Employee Number 只在 Domain constructor 指派一次。
- 保留 `UX_Employees_EmployeeNumber` 唯一索引，作為對所有既有與新 Employee Number 的權威碰撞防線。
- 依人工核准移除 application-level collision pre-check。這是移除冗餘且會造成死鎖的預先查詢，不是降低完整性。
- 已知唯一索引拒絕轉譯為受控 `EmployeeNumberGenerationException`。不 retry、不再取下一個 Sequence value、不使用 `MAX + 1`、manual override、static lock 或 `SemaphoreSlim`。
- Employee 與 AuditLog 維持既有原子交易；失敗後保留 `ChangeTracker.Clear()` 清理。診斷資訊不得暴露 SQL、connection string、stack trace 或個人資料。
- Sequence 配號不隨交易 rollback。失敗、取消、rollback 或復原造成的缺號可接受；已配發或已提交編號永不重用。

## Consequences

SQL Server Sequence 與 unique index 共同提供跨程序與多執行個體完整性。系統不保證連號，且到達 EMP9999 時會安全失敗。Migration Down 在 Sequence 曾被使用後具有 high-water mark 風險，沒有核准備份還原或 recovery 計畫時不得執行。

真實 Migration、Sequence、並行與強制碰撞測試只在本機 SQLEXPRESS 上符合 `HRSystem_Phase51_Test_<unique suffix>` 的全新 disposable database 執行，使用合成資料並在 `finally` 依嚴格 allow-list 清理；共享 `HRSystemDb` 不用於可重複的 Sequence 消耗或 restart 測試。

## Rejected Alternatives

- `MAX(EmployeeNumber) + 1`：並行不安全。
- application/process-local lock：無法涵蓋多程序或多執行個體。
- insert 前 collision query：Serializable key-range lock 造成 SQL Server 1205。
- 自動 retry 或再配發下一號：可能隱藏資料／部署衝突並消耗額外號碼。
- 人工輸入或 override：破壞自動配號與不可變契約。
