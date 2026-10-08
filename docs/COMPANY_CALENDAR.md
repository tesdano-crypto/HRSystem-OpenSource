# 公司行事曆

## 使用範圍

Phase 6 提供全公司共用的台灣民用日期行事曆。Admin、Manager、Employee 可讀取 Published 年度；只有 Admin 可驗證 Manifest、建立 Draft、發布、封存與異動日期。

本模組不處理輪班、個人假日資格、出勤、薪資或 LeaveRequest 時數重算，也不修改 Employee 或 LeaveRequest。

## 日期與分類

- 日期使用 `DateOnly`／SQL `date`，不經 UTC 轉換。
- 平日基準為 `WorkingDay`，星期六／日分別為 `Saturday`／`Sunday`。
- 官方例外只可為 `NationalHoliday` 或 `SubstituteHoliday`。
- 公司人工異動使用 `CompanyHoliday` 或 `ExceptionalWorkingDay`。
- `IsWorkingDay` 只由 `DayType` 推導；`WorkingDay`、`ExceptionalWorkingDay` 為 true，其餘為 false。
- 週末若同時是國定假日，有效 DayType 為 `NationalHoliday`；星期資訊仍由 Date 推導。

## Manifest

Repository-reviewed 檔案：

- `data/company-calendar/2026.json`
- `data/company-calendar/2027.json`

格式要求：

- schemaVersion `1.0`
- UTF-8 without BOM、LF、兩空格縮排、固定 property order
- date entries 遞增、無重複、同年度
- strict JSON，不接受未知欄位、comment 或 trailing comma
- 最大 256 KiB
- 官方 URL 必須是 DGPA HTTPS 網域
- ManifestHash 是 canonical bytes 的小寫 SHA-256

官方附件 SHA-256：

- 2026：`30f3314c8a217a741733a6a48cfeab9a0f8915f9fa0f90169fcbebf42f15c6d8`
- 2027：`5c63b9d5b5dfe2ce91a29e607bed2597d0bf811be02accc15920c5dac2b730a1`

Repository Manifest SHA-256：

- 2026：`e7ecf447b6216cbeef5ff01f580f0e3ef62bdb3d0a5fbb2a22f94ab9b86f0372`
- 2027：`9c401f17d002ad4eeccd0ad8d5c0e0a59ae489548785bc470472fadc6bc0858a`

## Lifecycle 與並行

- 新年度只能建立為 Draft。
- 只有完整 365／366 日、來源與 Manifest 一致的 Draft 可以發布。
- Published 可封存；Archived 不可回復或變更。
- 一般業務查詢只讀 Published；Archived 只供明確歷史畫面。
- Year 與 Day 使用 RowVersion；任何 Day 異動也 Touch Year。
- Calendar commands 使用 ReadCommitted；唯一年度／日期索引為跨程序權威防線。
- 不使用 static lock、SemaphoreSlim、process-local lock 或自動 retry。

## 人工異動與 AuditLog

人工異動必須有原因。移除 CompanyHoliday／ExceptionalWorkingDay 時，既有 Day row 還原到保存的官方／週末基準，不刪除日期。

初始化、Manifest 修訂、發布、封存記錄 aggregate AuditLog；日期變更記錄 before/after、工作狀態、名稱與原因。資料異動與 AuditLog 在同一 SaveChanges／transaction 中提交。

## Installation

Apply the schema only to your own database, validate the calendar manifest,
then explicitly initialize and publish each required year. This source snapshot
contains no published runtime calendar data or deployment evidence.
