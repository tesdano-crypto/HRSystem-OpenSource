using HRSystem.Domain.Attendance;
using HRSystem.Domain.AttendanceExceptions;
using HRSystem.Domain.Common;

namespace HRSystem.UnitTests;

public sealed class AttendanceExceptionTests
{
    private static readonly DateOnly Date = new(2026, 8, 12);
    private static readonly AttendanceShiftSnapshot Shift = new(Guid.NewGuid(), "正常班",
        new TimeOnly(8,0), new TimeOnly(8,1), new TimeOnly(12,0), new TimeOnly(13,30),
        new TimeOnly(17,30), 480, false, false);

    [Fact]
    public void Draft_Transitions_Are_Explicit_And_History_Is_Append_Only_Model()
    {
        var request = Request();
        request.Submit("employee", DateTimeOffset.UtcNow);
        request.Approve("manager", DateTimeOffset.UtcNow);
        request.RequestCancellation("資料更正", "employee", DateTimeOffset.UtcNow);
        request.RejectCancellation("仍有效", "manager", DateTimeOffset.UtcNow);
        Assert.Equal(AttendanceExceptionStatus.Approved, request.Status);
        Assert.Throws<DomainValidationException>(() => request.Submit("employee", DateTimeOffset.UtcNow));
        var history = new AttendanceExceptionHistory(Guid.NewGuid(), request.Id,
            AttendanceExceptionHistoryAction.CancellationRejected, "manager", "主管", "仍有效",
            DateTimeOffset.UtcNow, AttendanceExceptionStatus.CancellationRequested,
            AttendanceExceptionStatus.Approved);
        Assert.Equal(AttendanceExceptionHistoryAction.CancellationRejected, history.Action);
    }

    [Fact]
    public void FullDay_Has_No_Attendance_Anomalies_And_Is_Not_Leave()
    {
        var result = Calculate(Exception(AttendanceExceptionImpactType.FullDay), []);
        Assert.Equal(AttendanceDailyStatus.AttendanceExempted, result.Status);
        Assert.Equal(480, result.RequiredAttendanceMinutes);
        Assert.Equal(480, result.AttendanceExceptionMinutes);
        Assert.Equal(0, result.ApprovedLeaveMinutes);
        Assert.Empty(result.LeaveSegments);
        Assert.Equal(0, result.MissingMinutes);
        Assert.False(result.IsLate); Assert.False(result.IsEarlyLeave);
        Assert.False(result.MissingClockIn); Assert.False(result.MissingClockOut);
    }

    [Fact]
    public void LateArrival_Only_Waives_Time_Through_Approved_End()
    {
        var result = Calculate(Exception(AttendanceExceptionImpactType.LateArrival, to:new TimeOnly(10,0)),
            [Punch(10,30), Punch(17,30)]);
        Assert.True(result.IsLate);
        Assert.Equal(30 * 60, result.LateSeconds);
        Assert.Equal(120, result.AttendanceExceptionMinutes);
        Assert.Equal(30, result.MissingMinutes);
    }

    [Fact]
    public void LateArrival_At_Approved_End_Is_Not_Late()
    {
        var result = Calculate(Exception(AttendanceExceptionImpactType.LateArrival, to:new TimeOnly(10,0)),
            [Punch(10,0), Punch(17,30)]);
        Assert.False(result.IsLate);
        Assert.Equal(0, result.MissingMinutes);
    }

    [Fact]
    public void EarlyDeparture_Only_Waives_Time_After_Approved_Start()
    {
        var result = Calculate(Exception(AttendanceExceptionImpactType.EarlyDeparture, from:new TimeOnly(16,0)),
            [Punch(8,0), Punch(15,30)]);
        Assert.True(result.IsEarlyLeave);
        Assert.Equal(30 * 60, result.EarlyLeaveSeconds);
        Assert.Equal(90, result.AttendanceExceptionMinutes);
        Assert.Equal(30, result.MissingMinutes);
    }

    [Fact]
    public void EarlyDeparture_At_Approved_Start_Is_Not_Early()
    {
        var result = Calculate(Exception(AttendanceExceptionImpactType.EarlyDeparture, from:new TimeOnly(16,0)),
            [Punch(8,0), Punch(16,0)]);
        Assert.False(result.IsEarlyLeave);
        Assert.Equal(0, result.MissingMinutes);
    }

    [Fact]
    public void Leave_Is_Subtracted_Before_Exception_And_No_Double_Counting_Occurs()
    {
        var leaveId=Guid.NewGuid();var typeId=Guid.NewGuid();
        var leave = new AttendanceApprovedLeaveInterval(leaveId,typeId,"ANNUAL","特休",
            Date.ToDateTime(new TimeOnly(8,0)),Date.ToDateTime(new TimeOnly(12,0)));
        var result=AttendanceDailyCalculator.Calculate(Date,true,Shift,[],[leave],null,
            Exception(AttendanceExceptionImpactType.FullDay));
        Assert.Equal(240,result.ApprovedLeaveMinutes);
        Assert.Equal(240,result.RequiredAttendanceMinutes);
        Assert.Equal(240,result.AttendanceExceptionMinutes);
        Assert.Single(result.LeaveSegments);
        Assert.Equal(0,result.MissingMinutes);
    }

    [Fact]
    public void EmploymentSuspension_Has_Priority_Over_Exception()
    {
        var result=AttendanceDailyCalculator.Calculate(Date,true,Shift,[],[],Guid.NewGuid(),
            Exception(AttendanceExceptionImpactType.FullDay));
        Assert.True(result.IsEmploymentSuspended);
        Assert.False(result.IsAttendanceExempted);
        Assert.Equal(0,result.AttendanceExceptionMinutes);
    }

    [Fact]
    public void Raw_Punches_Are_Preserved_During_FullDay_Exception()
    {
        var first=Punch(8,0);var last=Punch(17,30);
        var result=Calculate(Exception(AttendanceExceptionImpactType.FullDay),[first,last]);
        Assert.Equal(first.EventId,result.RawClockIn?.EventId);
        Assert.Equal(last.EventId,result.RawClockOut?.EventId);
        Assert.Equal(480,result.RecognizedWorkMinutes);
    }

    [Fact]
    public void Partial_Exception_Clips_To_Work_Intervals_And_Excludes_Lunch()
    {
        var result=Calculate(Exception(AttendanceExceptionImpactType.LateArrival,to:new TimeOnly(14,0)),[Punch(14,0),Punch(17,30)]);
        Assert.Equal(270,result.AttendanceExceptionMinutes);Assert.False(result.IsLate);Assert.Equal(0,result.MissingMinutes);
    }

    [Fact]
    public void Rest_Day_Does_Not_Project_Exception_Minutes()
    {
        var result=AttendanceDailyCalculator.Calculate(Date,false,Shift,[],[],null,Exception(AttendanceExceptionImpactType.FullDay));
        Assert.Equal(AttendanceDailyStatus.RestDay,result.Status);Assert.False(result.IsAttendanceExempted);Assert.Equal(0,result.AttendanceExceptionMinutes);
    }

    [Fact]
    public void Existing_Adjustment_Recalculates_Without_Losing_Exception_Metadata()
    {
        var raw=Calculate(Exception(AttendanceExceptionImpactType.LateArrival,to:new TimeOnly(10,0)),[]);
        var adjusted=AttendanceDailyCalculator.ApplyEffectiveTimes(Date,Shift,raw,Date.ToDateTime(new TimeOnly(10,0)),Date.ToDateTime(new TimeOnly(17,30)));
        Assert.True(adjusted.IsAttendanceExempted);Assert.Equal(raw.AttendanceExceptionSourceId,adjusted.AttendanceExceptionSourceId);
        Assert.Equal(120,adjusted.AttendanceExceptionMinutes);Assert.False(adjusted.IsLate);
    }

    [Theory]
    [InlineData(AttendanceExceptionImpactType.FullDay, 1, 0)]
    [InlineData(AttendanceExceptionImpactType.LateArrival, 1, 0)]
    [InlineData(AttendanceExceptionImpactType.EarlyDeparture, 0, 1)]
    public void Impact_Time_Shape_Is_Validated(AttendanceExceptionImpactType impact,int from,int to)
    {
        Assert.Throws<DomainValidationException>(() => new AttendanceException(Guid.NewGuid(),"AE-X",
            Guid.NewGuid(),Date,NaturalDisasterReasonType.WorkplaceClosure,impact,
            from==1?new TimeOnly(9,0):null,to==1?new TimeOnly(9,0):null,null,"user",DateTimeOffset.UtcNow));
    }

    private static AttendanceException Request()=>new(Guid.NewGuid(),"AE-TEST",Guid.NewGuid(),Date,
        NaturalDisasterReasonType.WorkplaceClosure,AttendanceExceptionImpactType.FullDay,null,null,
        "颱風停班","employee",DateTimeOffset.UtcNow);
    private static AttendanceApprovedException Exception(AttendanceExceptionImpactType impact,TimeOnly? from=null,TimeOnly? to=null)=>
        new(Guid.NewGuid(),AttendanceExceptionType.NaturalDisaster,NaturalDisasterReasonType.WorkplaceClosure,impact,from,to);
    private static AttendancePunchCandidate Punch(int hour,int minute)=>new(Guid.NewGuid(),Date.ToDateTime(new TimeOnly(hour,minute)));
    private static AttendanceDailyCalculation Calculate(AttendanceApprovedException exception,IEnumerable<AttendancePunchCandidate> punches)=>
        AttendanceDailyCalculator.Calculate(Date,true,Shift,punches,[],null,exception);
}
