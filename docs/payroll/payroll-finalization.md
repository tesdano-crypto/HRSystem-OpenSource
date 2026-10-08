# 薪資正式結算與薪資單

## 邊界

- 薪資簽核核准只表示核准當下的月份指紋；不等於正式結算。
- 正式結算只允許 `PayrollFinalize`，目前角色矩陣僅 Admin 具備。
- 正式結算必須在單一 Serializable transaction 內完成，且同一薪資月份只能有一筆。
- 結算後月份狀態為 `Finalized`；既有試算、調整與送簽服務會在伺服器端拒絕變更。

## 結算 Gate

結算前必須同時符合：

1. 所有任職期間涵蓋該月的員工都有 current snapshot。
2. current pointer 未標記 `SourceChanged`。
3. 每份快照為 `Resolved`、無 blocking evidence、NetPay 非 null 且不為負數。
4. 重新依快照元件計算的應發、扣款、實領與 total fingerprint 均一致。
5. 存在狀態為 Approved、來源月份相同且 fingerprint 完全相符的 Payroll Approval。

## 不可變憑證

`PayrollFinalization` 保存核准人、結算人、核准時間、結算時間、月份指紋與總額。
`PayrollFinalEmployeeSnapshot` 明確固定每名員工當時的 `PayrollEmployeeSnapshotId`，並複製歷史顯示用員工識別、部門、總額與快照指紋。

薪資單只從此固定映射及其不可變 snapshot components 產生；不得重新計算，也不得跟隨 current pointer。零額元件不顯示。

## 權限與隱私

- `PayrollView` 可查看所有正式結算與薪資單。
- `PayslipViewSelf` 只可查看登入帳號綁定 EmployeeId 的薪資單；Employee 與 Manager 均不會取得部屬薪資權限。
- 薪資單識別使用 opaque GUID，Application service 仍會逐筆驗證 EmployeeId。
- Audit 僅記錄結算摘要、總額與指紋，不記錄逐員工明細。

## Migration 與回復

Migration `AddPayrollFinalization` 新增兩張表、NoAction FK、唯一 Period／Approval／Employee／Snapshot 索引，不更新既有業務資料。若已有正式結算資料，Down 會拒絕；正式回復應使用 migration 前完整資料庫備份與對應 Published 備份。
