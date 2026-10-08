# ADR-0004：UTC 儲存與 Taipei 顯示

Status: Accepted

## Context

請假區間需要一致比較，同時符合台灣內網使用者的日期理解。

## Decision

資料庫以 DateTimeOffset UTC 儲存，UI 以 Asia/Taipei 顯示與輸入；「今日」先建立 Taipei 日期邊界再轉為 UTC 查詢。

## Consequences

跨午夜與日期邊界可一致處理；所有新時間查詢都必須明確套用相同轉換規則。
