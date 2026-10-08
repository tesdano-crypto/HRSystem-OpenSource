# 保險投保資料維護

勞保、災保與健保是由會計或人資維護的 effective-dated 權威主檔；Payroll calculator 只讀取已確認的投保資料，不會由薪資金額自行推算或靜默補值。

## 欄位與日期語意

- 勞保、職業災害保險與健保分別使用獨立的 effective-dated enrollment；災保可在沒有一般勞保時單獨存在。
- 健保投保金額不會由薪資或勞／災保自動複製；健保眷屬人數必須由維護者確認。
- 「加保日」是 coverage 開始日。
- UI 的「不再投保生效日」採 exclusive 語意：該日起不再由本公司投保。現有資料模型的 `EffectiveTo` 為 inclusive，Application Service 會將輸入日期減一天後保存。
- 新級距以新 effective row 表示；舊 row 只會結束有效期間，不會被覆寫或刪除。
- gap 可以代表未投保期間，preview 會提示但不強迫連續；overlap 則由 server-side validation 拒絕。
- 歷史離職員工與月中加保日期均可維護，不能以目前在職狀態或到職日取代保險權威資料。

## 計費日期規則

### 勞保／就保

- coverage 只依保險 `EffectiveFrom`／`EffectiveTo` 判斷，不以 `HireDate` 或 `TerminationDate` 靜默裁切。
- 已確認採 30 日制的政策使用實際投保日數，固定以 30 為分母；2 月及 31 日月份整月均為 `30/30`。
- 月中加保、退保由共用 `InsuranceCoverageDays` 計算；例如 7/13 加保且月底有效為 `18/30`，不再投保生效日 7/21（stored `EffectiveTo` 7/20）為 `20/30`。
- 勞保普通事故與就保沿用相同 coverage factor，各自保留既有費率及員工負擔比例。

### 健保

- 健保按月計費，不使用 `/30`；月中加保且月底仍有效，該月計收整月。
- 月底有本公司 `Enrolled` authority 時為 `CoveredFullMonth`；月底有明確 `NotEnrolled` 或有效資料顯示本公司不承保時為 `NotCovered`。
- 月中轉出且只有本公司較早的 enrollment、缺少月底投保單位 authority 時為 `NeedsReview`，不推定整月或零元。
- enrollment 資料足夠但正式費率政策缺少時才是 `PolicyPending`；完全沒有 enrollment 設定是 `NeedsSetup`。

### 職業災害保險

- 災保 coverage 由獨立 `EmployeeOccupationalInsuranceEnrollment` 的日期與投保薪資決定，不依賴或複製勞保 enrollment。
- coverage days 可重用 `InsuranceCoverageDays` 表達 30 日制因子，但本階段不建立官方費率或保費扣款。
- 有災保 enrollment 但尚無正式政策時為 `PolicyPending`；完全未設定為 `NeedsSetup`。這兩種狀態不會使合法的「僅災保」員工被誤判為勞保資料錯誤。
- 薪資設定的唯讀試算會讀取獨立災保來源，顯示當月 coverage 與 readiness；不建立保費、扣款或快照。災保來源使用獨立 v1 fingerprint；新勞保計算改用不含災保資料的 v2 fingerprint，既有不可變快照保留原版本與內容。
- 沒有 `InsuranceView` 權限者不會取得新的災保 preview 資料。UI 不顯示來源 fingerprint。

### Insurance authority

- 加保日不等同到職日，退保／不再投保日期不等同離職日。
- UI 或 validation 可提示不尋常的日期組合；calculator 一律尊重已由 Application Service 接受的保險有效日期。
- 是否應投保由會計輸入的正式保險資料決定；系統不新增退休旗標，也不以年齡推定勞保或災保資格。

官方規則參考：

- 勞動部勞工保險局勞／就／災保費試算說明（加保日至退保日、每月 30 日制）：https://www.bli.gov.tw/0014162.html
- 衛生福利部中央健康保險署投保與計費說明（按月、月底投保單位計收）：https://www.nhi.gov.tw/ch/cp-3204-6ecca-2568-1.html

## 會計操作流程

1. 在 `/admin/insurance` 搜尋員工，可切換顯示歷史離職員工或只看待設定資料。
2. 分別輸入勞保／就保、職業災害保險或健保資料及必填維護原因。
3. 先按「預覽／驗證」，確認將新增／結束的 row、gap、受影響 Payroll 月份與 finalized 保護提示。
4. 內容正確後才按「確認套用」。若資料自預覽後已變更，系統要求重新預覽。
5. 套用會寫入 append-only Audit；未 finalized 的既有薪資試算只會標記「薪資資料已變更，需重新試算」，不會自動重算。已 finalized 薪資保持不可變。

## 政策與權限

- 本功能只維護 enrollment、投保金額與眷屬，不建立保費率、員工／雇主負擔比例或官方級距政策。
- 有足夠 enrollment 但缺少正式政策時，Payroll 顯示 `PolicyPending`；缺少 enrollment 才是 `NeedsSetup`；健保月底 authority 不足時則顯示 `NeedsReview`。
- Admin、Accounting、HR 依既有 `InsuranceManage` 權限維護；Owner 與一般 Employee 不因薪資簽核或自助權限而取得保險管理權。
- 未來 Excel／CSV 匯入必須重用 Application Service 的 validation、preview、apply 與 Audit，不能把規則只寫在 Razor。
