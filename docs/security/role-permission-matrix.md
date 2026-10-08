# 角色與權限矩陣

HRSystem 以穩定英文代碼保存角色與權限，功能路由與 Application Service
以 permission policy 授權；一般使用者介面只顯示本文件中的繁體中文名稱。

## 角色

| 內部角色 | 中文名稱 | 定位 |
|---|---|---|
| `Admin` | 系統管理員 | 擁有全部權限 |
| `HR` | 人資 | 人事、出勤、請假、加班與勞健保管理；薪資唯讀 |
| `Accounting` | 會計 | 出勤至薪資送簽前作業；不可自行核准或正式結算 |
| `Owner` | 老闆 | 薪資唯讀與簽核權限基礎；不可維護薪資或系統資料 |
| `Manager` | 主管 | 維持既有部門範圍核准與查詢 |
| `Employee` | 一般員工 | 維持既有個人自助功能 |

使用者可以有多個角色，實際權限是所有角色權限的聯集。未知角色與未知
permission 均拒絕。現有使用者不會由程式自動改派新角色。

## 核心權限中文名稱

| 功能 | Permission | 中文名稱 |
|---|---|---|
| 人事 | `EmployeeView` | 查看員工資料 |
| 人事 | `EmployeeManage` | 員工資料管理 |
| 出勤 | `AttendanceViewAll` | 查看全員出勤紀錄 |
| 出勤 | `AttendanceDailyReport` | 每日出勤報表 |
| 出勤 | `AttendanceManage` | 出勤資料管理 |
| 請假 | `LeaveView` | 查看請假資料 |
| 請假 | `LeaveManage` | 請假管理 |
| 加班 | `OvertimeView` | 查看加班資料 |
| 加班 | `OvertimeManage` | 加班管理 |
| 勞健保 | `InsuranceView` | 查看勞健保資料 |
| 勞健保 | `InsuranceManage` | 勞健保管理 |
| 薪資 | `PayrollView` | 薪資查看 |
| 薪資 | `PayrollManage` | 薪資管理 |
| 薪資 | `PayrollSubmitApproval` | 送出薪資簽核 |
| 薪資 | `PayrollApprove` | 薪資簽核核准 |
| 薪資 | `PayrollFinalize` | 薪資正式結算 |
| 簽核 | `ApprovalView` | 查看待簽核事項 |
| 簽核 | `ApprovalAct` | 執行簽核 |
| 簽核 | `ApprovalHistoryView` | 歷史簽核查詢 |
| 系統管理 | `UserAdmin` | 使用者與權限管理 |
| 系統管理 | `SystemAdmin` | 系統管理 |

## 預設角色矩陣

| 權限 | 系統管理員 | 人資 | 會計 | 老闆 |
|---|:---:|:---:|:---:|:---:|
| `EmployeeView` | ✓ | ✓ | ✓ | — |
| `EmployeeManage` | ✓ | ✓ | — | — |
| `AttendanceViewAll` / `AttendanceDailyReport` / `AttendanceManage` | ✓ | ✓ | ✓ | — |
| `LeaveView` / `LeaveManage` | ✓ | ✓ | ✓ | — |
| `OvertimeView` / `OvertimeManage` | ✓ | ✓ | ✓ | — |
| `InsuranceView` / `InsuranceManage` | ✓ | ✓ | ✓ | — |
| `PayrollView` | ✓ | ✓ | ✓ | ✓ |
| `PayrollManage` / `PayrollSubmitApproval` | ✓ | — | ✓ | — |
| `PayrollApprove` | ✓ | — | — | ✓ |
| `PayrollFinalize` | ✓ | — | — | — |
| `ApprovalView` | ✓ | ✓ | ✓ | ✓ |
| `ApprovalAct` / `ApprovalHistoryView` | ✓ | — | — | ✓ |
| `UserAdmin` / `SystemAdmin` | ✓ | — | — | — |

主管與一般員工繼續使用既有最小權限集合：主管僅限自己的部門範圍，
一般員工僅限自己的出勤、請假、加班與個人資料。兩者都沒有全員出勤、
薪資、勞健保管理或使用者管理權限。

## 維護規則

- Permission 定義、角色集合與中文名稱分別集中在 `PolicyNames`、
  `RolePermissions`、`SecurityDisplayCatalog`，不得在 Razor 複製另一套矩陣。
- UI 隱藏操作只是體驗；路由 policy 與 Application Service 都必須驗證權限。
- 角色異動沿用使用者管理既有 Audit，記錄目標、異動前後角色、操作者與時間。
- 新增 permission 時必須同步加入集中式 policy 註冊、中文對照與角色矩陣測試。
- `PayrollSubmitApproval`、`PayrollApprove`、`ApprovalView` 與 `ApprovalAct`
  已由共用簽核基礎使用；`PayrollFinalize` 仍只是授權保留，不代表正式結算已實作。
- 薪資送簽人與核准人必須分權；Accounting 不可核准，Owner 不可維護或送出薪資。
- LINE 決策仍須通過同一 Application permission 與來源指紋驗證，不得只靠 UI、
  LineUserId 或通知 token 放行。
