-- Read-only post-migration validation. Run only under separately approved DB access,
-- in a write-free cutover window. Output contains counts only, no employee identifiers.
-- All three counts must be zero. Any mismatch: STOP, never repair by adding credits.
WITH Returned AS (
    SELECT a.Id FROM dbo.CompTimeAllocations a
    JOIN dbo.CompTimeAllocationReturns r ON r.AllocationId = a.Id
), Credit AS (
    SELECT t.EmployeeId, t.Hours - COALESCE((
        SELECT SUM(a.AllocatedHours) FROM dbo.CompTimeAllocations a
        WHERE a.GrantTransactionId = t.Id AND NOT EXISTS (SELECT 1 FROM Returned r WHERE r.Id = a.Id)
    ), 0) AS Remaining
    FROM dbo.CompTimeTransactions t WHERE t.TransactionType = 1
      AND NOT EXISTS (SELECT 1 FROM dbo.CompTimeLegacyMembers m WHERE m.TransactionId = t.Id)
    UNION ALL
    SELECT p.EmployeeId, p.GrantedHours + p.RestoredHours - p.ConsumedHours
      + COALESCE((SELECT SUM(t.Hours) FROM dbo.CompTimeLegacyReturns r
          JOIN dbo.CompTimeTransactions t ON t.Id = r.RestoreTransactionId WHERE r.PoolId = p.Id), 0)
      - COALESCE((SELECT SUM(a.AllocatedHours) FROM dbo.CompTimeAllocations a
          WHERE a.LegacyPoolId = p.Id AND NOT EXISTS (SELECT 1 FROM Returned r WHERE r.Id = a.Id)), 0)
    FROM dbo.CompTimeLegacyPools p
), Official AS (
    SELECT EmployeeId, SUM(CASE WHEN TransactionType = 2 THEN -Hours ELSE Hours END) AS Balance
    FROM dbo.CompTimeTransactions GROUP BY EmployeeId
), Available AS (
    SELECT EmployeeId, SUM(Remaining) AS Balance FROM Credit GROUP BY EmployeeId
)
SELECT
    (SELECT COUNT(*) FROM Official o FULL JOIN Available a ON o.EmployeeId = a.EmployeeId
        WHERE COALESCE(o.Balance, 0) <> COALESCE(a.Balance, 0)) AS BalanceMismatchCount,
    (SELECT COUNT(*) FROM Credit WHERE Remaining < 0) AS NegativeCreditCount,
    (SELECT COUNT(*) FROM dbo.CompTimeTransactions t
        WHERE t.TransactionType = 2
          AND NOT EXISTS (SELECT 1 FROM dbo.CompTimeLegacyMembers m WHERE m.TransactionId = t.Id)
          AND t.Hours <> COALESCE((SELECT SUM(a.AllocatedHours) FROM dbo.CompTimeAllocations a
              WHERE a.ConsumeTransactionId = t.Id), 0)) AS UnreconciledConsumeCount;
