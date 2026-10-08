namespace HRSystem.Domain.Attendance;

public enum BioWebTaImportTriggerType : byte
{
    Scheduled = 1,
    Manual = 2
}

public enum BioWebTaImportBatchStatus : byte
{
    Running = 1,
    Completed = 2,
    CompletedWithWarnings = 3,
    Failed = 4,
    Abandoned = 5
}

public enum BioWebTaImportIssueCode : byte
{
    SourceEventContentMismatch = 1,
    InvalidSourceRecord = 2,
    UncomputableFingerprint = 3,
    PersistenceFailure = 4,
    RecalculationFailure = 5,
    UnexpectedFailure = 6
}
