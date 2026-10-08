# Phase 4 請假申請與核准流程

## 狀態機

```text
Draft ──Submit──> Submitted ──Approve──> Approved
                         ├──Reject────> Rejected
                         └──Withdraw──> Withdrawn
```

- Draft：本人可修改、刪除或送出。
- Submitted：本人不可修改內容，但可撤回；Manager/Admin 可核准或退回。
- Approved、Rejected、Withdrawn：終止狀態，不直接回到 Draft。
- 任一申請可在有權查看時複製為新 Draft，原單與歷程不變。

非法狀態轉移由 Domain Entity 拒絕，Application Service 再負責身分、資料範圍與跨資料列規則。

## 權限與資料範圍

- Employee：只可查詢與操作自己的申請。
- Manager：只可處理同部門其他員工的 Submitted 申請。
- Admin：可處理全部 Submitted 申請，並可唯讀查詢全部狀態。
- 所有人都不可核准或退回自己的申請。
- 沒有綁定 EmployeeId 的一般帳號不能使用請假自助功能。
- 頁面上的 Authorize Policy 不是唯一防線；Application Service 對每個操作重新驗證。

## 建立與送出規則

- Employee 與 LeaveType 必須存在且為啟用狀態。
- StartAt 必須早於 EndAt。
- 單筆期間不得超過 31 天。
- DurationHours 由共用 Application 計算服務逐日取得 Employee 的唯一有效 EmployeeShiftAssignment 與啟用 Shift，將請假區間分別與午休前、午休後工作區間取交集後加總，四捨五入至兩位小數且必須大於 0。
- 午休、班別以外時間與非工作日不計入；非工作日預估為 0 並顯示警告。
- 工作日沒有有效班別、班別無效或同日有超過一筆有效指派時不猜測時數，草稿頁顯示處理訊息且不可儲存或送出。
- Reason 必填且最長 500 字元。
- Draft 頁即時預估、草稿建立／更新與送出共用同一服務；送出時重新計算，核准前再次確認目前有效班別計算的時數與送出時一致。

## 假別停用生命週期

- 新申請的假別選項只包含啟用中的 LeaveType。
- LeaveType 停用不受既有 Draft 或 Submitted 申請阻擋，也不會自動修改既有申請。
- 既有 Draft 仍可閱讀，編輯頁會保留並標示原停用假別；保留原假別時可修改日期、時間或原因。
- Draft 使用停用假別時不可送出，必須先改選啟用中的 LeaveType。
- Submitted 申請即使假別之後停用，仍可依既有流程核准、退回或撤回。
- Approved、Rejected、Withdrawn 與其 LeaveApprovalHistory 保持有效且不變。

## 重疊檢查

相同 Employee 的新期間若與既有 Submitted 或 Approved 期間符合下式，即視為重疊：

```text
NewStart < ExistingEnd && NewEnd > ExistingStart
```

相鄰但不相交的期間允許。系統會在建立/修改、送出及核准前檢查；核准前重查可避免兩筆等待中的申請同時成為有效重疊紀錄。

## 時區

- Application/Domain 接受 `DateTimeOffset`，進入 Entity 時轉為 UTC。
- SQL Server 使用 `datetimeoffset` 保存 UTC。
- Blazor 輸入與顯示統一轉換為 `Asia/Taipei`。
- 正常班驗證案例 `2026-08-03 08:00–17:30`（Taipei）保存為 `00:00–09:30Z`；扣除 `12:00–13:30` 午休後計算為 8 小時。

## 核准歷程與稽核

每次 Submitted、Approved、Rejected、Withdrawn 都新增一筆 LeaveApprovalHistory，記錄操作者、顯示名稱、UTC 時間、FromStatus、ToStatus 與必要意見。

- Rejected 必須有 Comment。
- History 已建立後不得修改或刪除；DbContext 會拒絕 Modified/Deleted 狀態。
- LeaveRequest 狀態、History 與 AuditLog 在同一個 `SaveChangesAsync` 中保存。
- AuditLog 記錄動作與非敏感狀態快照，不保存 Reason、Comment、密碼、Token 或 Connection String。

## 頁面

- `/leave-requests`：我的請假與查詢條件
- `/leave-requests/new`：建立草稿
- `/leave-requests/{id}`：內容與完整歷程
- `/leave-requests/{id}/edit`：修改草稿
- `/approvals/leave`：Manager/Admin 待簽與已處理清單
- `/admin/leave-requests`：Admin 全部申請唯讀查詢

## 資料庫與測試隔離

- Migration：`20260719015020_AddLeaveRequestWorkflow`
- Development：`.\SQLEXPRESS / HRSystemDb`、Windows Integrated Authentication
- Phase 4 未新增 LeaveRequest Seed；驗證資料由正常 Application Service 建立。

## 本階段不包含

年假額度、結轉、出勤/打卡、排班、加班、多層可配置簽核、代理人、附件、Email/LINE 通知、報表、AI 與正式公司資料庫部署。
