# 共用簽核與 LINE 私訊基礎

## 邊界

共用簽核以 `Approval`、append-only `ApprovalHistory`、`LineUserBinding` 與
`ApprovalLineActionToken` 為核心。第一個來源是 `PayrollRun`，後續模組必須透過
`IApprovalSourceProvider` 提供可信來源快照，不得把模組規則複製到簽核服務。

送簽只建立簽核，不代表薪資正式結算。核准、退回、取消或來源失效亦不會執行
Payroll Finalize；正式結算仍屬後續獨立階段。

## 狀態與資料一致性

- `Pending -> Approved | Returned | Cancelled | Superseded`，完成後不可重複決策。
- 同一來源、來源版本同時只能有一筆 Pending；相同快照重送回傳既有簽核，
  快照已變更則先將舊 Pending 標記 Superseded，再建立新簽核。
- 送簽保存 SHA-256 來源指紋。Web 與 LINE 決策前都重新取得來源快照；不相符時
  只能 Supersede，不可核准。
- 狀態、History、Audit 與 token 消耗／撤銷在同一資料庫交易內完成。
- LINE 通知在送簽交易 commit 後執行。通知失敗只標記 `Failed` 並保留 Pending，
  不回滾或重建簽核；重試通知只換發 token，不新增 Approval。

## Payroll v1 快照

`PayrollApprovalSourceProvider` 只接受所有員工 totals 均為 `Resolved` 且具有
32-byte total source fingerprint 的 Draft。摘要只包含月份、員工數、應發總額、
扣款總額與實領總額，不包含員工逐筆薪資。

`payroll-approval-v1` 指紋以固定順序涵蓋 Run、月份、狀態及每位員工快照的
total fingerprint/version 與三項總額。任何來源快照變動都會令既有 Pending 失效。

## LINE 私訊安全模型

- HRSystem 不把排程或簽核邏輯綁入 Web background timer；已設定的 LINE 助理仍是唯一
  LINE Messaging API webhook/signature authority。
- HRSystem 只透過設定的 HTTPS bridge endpoint 發送 `targetType=user` 私訊；沒有
  群組、聊天室或 fallback 目標。
- bridge 回呼 `/api/integrations/line/approval-postback` 必須帶共享 secret header；
  未設定或不符即拒絕。secret 只由 User Secrets／環境變數提供。
- Approve/Return 動作由 token 本身綁定，不從 LLM 或自由文字推論。
- token 是 32-byte CSPRNG opaque value；資料庫只保存 SHA-256 hash，並綁定
  Approval、action、預定簽核人、LineUserId 與 48 小時有效期。完成時 consume 並
  revoke 同簽核其他 token，防止 replay。
- 退回原因仍必填；文字只作原因，不決定 action。
- LINE 綁定只接受 Messaging API `source.type=user` 的私人帳號；`group` 與 `room`
  一律拒絕。一般瀏覽器與管理 UI 不接受手動輸入 `LineUserId`。
- SystemAdmin 從 `/admin/line-bindings` 為已啟用且具有 Owner 角色的帳號建立
  20 分鐘一次性配對碼。碼值由 CSPRNG 產生且只顯示一次，資料庫僅保存
  SHA-256 hash；成功、逾期、撤銷後均不可 replay。
- 已設定的 LINE 助理解析私人訊息後，以受保護的
  `/api/integrations/line/private-pairing` callback 傳入配對碼、`LineUserId` 與
  `sourceType=user`。callback 使用與 approval postback 相同的 fail-closed bridge
  secret，但 pairing token 與 `ApprovalLineActionToken` 是不同資料模型與用途。
- 每位 HRSystem 使用者及每個 LINE 私人帳號同時只允許一筆 active binding。
  更換及解除必須由 SystemAdmin 明確操作並填寫原因；舊 binding 只 soft revoke，
  不覆寫或刪除。UI、Audit 與 log 只顯示遮罩識別，不保存完整 LINE UserId。
- 所有 Owner 通知目標一律透過 `ILinePrivateTargetResolver` 由指定 HRSystem UserId
  解析 active verified private binding；不得以員工編號、姓名、群組或 fallback
  目標路由。

## 設定

設定區段 `ApprovalLineBridge`：

- `Enabled`：預設 false。
- `NotificationEndpoint`：已設定的 LINE 助理受保護的 private notification endpoint。
- `ApiKey`：bridge secret，禁止提交 repository。
- `ApiKeyHeaderName`：通知呼叫 header；回呼固定使用
  `X-HRSystem-Bridge-Key`，部署時兩端須一致。

未設定時送簽仍成功，但通知狀態為 Failed，送簽人可從簽核明細重試。

## 授權

- Accounting/Admin：`PayrollSubmitApproval`，可送薪資簽核與看自己的送簽。
- Owner/Admin：`ApprovalAct` + `PayrollApprove`，可處理指定簽核。
- HR：`ApprovalView` 但沒有薪資送簽／核准權；目前只會看到自己是送簽人或
  指定簽核人的項目，因此不會擴大薪資可見範圍。
- Manager/Employee：沒有 Approval permissions，路由與 Application 都拒絕。

## 驗證與部署 Gate

開發使用 `scripts/test-approval.ps1`。LINE pairing Migration 必須在 disposable SQL
Server 執行 32→33→32→33，確認既有 binding metadata 回填、FK NoAction、active
filtered unique index、token hash unique index與 Pending Model Changes none。正式 DB
Migration、LINE bridge 啟用、pairing、publish 與 deployment 均需另行授權。
