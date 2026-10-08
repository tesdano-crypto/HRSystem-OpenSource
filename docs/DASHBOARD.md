# Role-based Dashboard

## 目的

Dashboard 是公司內網的唯讀工作入口，使用既有 HR 與請假資料即時計算摘要。頁面不提供狀態變更或資料維護操作。

## 角色與資料範圍

- Employee：只顯示本人草稿、待審核、已核准、被退回數量、待辦及最近 5 筆申請。
- Manager：除本人資料外，只顯示其啟用 Employee 所屬部門內其他員工的待簽核與今日請假。此範圍沿用現有請假簽核模型，不使用 Department 的 ManagerEmployeeId，也不是全公司範圍。
- Admin：顯示全域待簽核、今日請假、啟用員工與啟用部門數量，以及既有管理功能入口。

未綁 Employee 的 Employee 帳號顯示友善空狀態，不會取得其他人的資料。未綁 Employee 的 Manager 無法建立部門範圍，因此顯示範圍不可用提示。Admin 即使未綁 Employee，仍可使用全域管理摘要，但不顯示「我的請假」。

## 今日請假

「今日」使用 `Asia/Taipei` 的本地日期邊界轉換為 UTC 後查詢。只計入 Approved，且請假區間須與今日區間重疊；Draft、Submitted、Rejected、Withdrawn 均不列入。

## 安全與唯讀限制

- Application Service 驗證角色與資料範圍，UI 不直接操作 DbContext。
- 查詢採投影、Count、GroupBy 與筆數上限，不載入不必要關聯。
- 不回傳或顯示完整請假原因、簽核意見、認證資料或其他敏感內容。
- Dashboard 不呼叫 SaveChanges、不修改 LeaveRequest／LeaveApprovalHistory，也不建立 AuditLog。
- 不查詢 AuditLog 明細，不建立快取、背景預計算或摘要資料表。
- 本階段未新增 Migration、Schema、Seed 或套件。

## 環境隔離與範圍外項目

Integration Tests 固定使用 Testing 與 EF Core InMemory，不讀 Development User Secrets，也不連 Development SQL。Development 僅使用本機 SQL Express 安全基線。

本階段不包含特休餘額、出勤、打卡、排班、通知、LINE、AI、多層簽核或新的報表功能。
