using Bunit;
using HRSystem.Application.AttendanceExceptions;
using HRSystem.Application.Common.Models;
using HRSystem.Domain.AttendanceExceptions;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class AttendanceExceptionWebTests : BunitContext
{
    private readonly StubService _service=new();
    public AttendanceExceptionWebTests(){var auth=AddAuthorization();auth.SetAuthorized("employee");Services.AddFluentUIComponents();Services.AddSingleton<IAttendanceExceptionService>(_service);JSInterop.Mode=JSRuntimeMode.Loose;}
    [Fact] public void Employee_Page_Clearly_Says_Exception_Is_Not_Leave(){var cut=Render<AttendanceExceptionNew>();cut.WaitForAssertion(()=>{Assert.Contains("不是請假",cut.Markup,StringComparison.Ordinal);Assert.Contains("全日豁免",cut.Markup,StringComparison.Ordinal);Assert.Contains("延後到班",cut.Markup,StringComparison.Ordinal);Assert.Contains("提前離開",cut.Markup,StringComparison.Ordinal);});}
    [Fact] public void FullDay_Form_Does_Not_Show_Time_Field(){var cut=Render<AttendanceExceptionNew>();cut.WaitForAssertion(()=>Assert.DoesNotContain("type=\"time\"",cut.Markup,StringComparison.OrdinalIgnoreCase));}
    [Fact] public void LateArrival_Shows_Exemption_End_Time(){var cut=Render<AttendanceExceptionNew>();cut.FindAll("select").Last().Change(AttendanceExceptionImpactType.LateArrival.ToString());cut.WaitForAssertion(()=>{Assert.Contains("豁免至",cut.Markup,StringComparison.Ordinal);Assert.Single(cut.FindAll("input[type=time]"));});}
    [Fact] public void EarlyDeparture_Shows_Exemption_Start_Time(){var cut=Render<AttendanceExceptionNew>();cut.FindAll("select").Last().Change(AttendanceExceptionImpactType.EarlyDeparture.ToString());cut.WaitForAssertion(()=>{Assert.Contains("自此時間起豁免",cut.Markup,StringComparison.Ordinal);Assert.Single(cut.FindAll("input[type=time]"));});}
    [Fact] public void Approval_Page_Shows_Employee_Date_Reason_And_Impact(){var cut=Render<AttendanceExceptionApprovals>();cut.WaitForAssertion(()=>{Assert.Contains("EMP9101",cut.Markup,StringComparison.Ordinal);Assert.Contains("工作所在地停止辦公",cut.Markup,StringComparison.Ordinal);Assert.Contains("全日豁免",cut.Markup,StringComparison.Ordinal);});}
    [Fact] public void Detail_Shows_Shift_Punch_And_Projected_Outcome(){var cut=Render<AttendanceExceptionDetail>(p=>p.Add(x=>x.Id,Guid.NewGuid()));cut.WaitForAssertion(()=>{Assert.Contains("原始排班",cut.Markup,StringComparison.Ordinal);Assert.Contains("原始 Punch 摘要",cut.Markup,StringComparison.Ordinal);Assert.Contains("預計豁免後結果",cut.Markup,StringComparison.Ordinal);});}
    [Fact] public void My_Page_Without_Employee_Binding_Shows_Warning_Without_Querying_Other_Data()
    {
        var cut=Render<AttendanceExceptionRequests>();
        cut.WaitForAssertion(()=>
        {
            Assert.Contains("目前帳號未綁定員工資料",cut.Markup,StringComparison.Ordinal);
            Assert.DoesNotContain("EMP9101",cut.Markup,StringComparison.Ordinal);
            Assert.Equal(0,_service.MyRequestsCallCount);
        });
    }
    [Fact] public void Daily_Attendance_Has_Structured_Exception_Display_And_Leave_UI_Does_Not_Add_Typhoon_Leave()
    {
        var root=RepositoryRoot();var daily=File.ReadAllText(Path.Combine(root,"src","HRSystem.Web","Components","Pages","DailyAttendance.razor"));
        Assert.Contains("AttendanceExceptionMinutes",daily,StringComparison.Ordinal);Assert.Contains("/attendance-exceptions/",daily,StringComparison.Ordinal);
        foreach(var file in new[]{"LeaveRequestNew.razor","LeaveTypes.razor"}){var path=Path.Combine(root,"src","HRSystem.Web","Components","Pages",file);if(File.Exists(path))Assert.DoesNotContain("颱風假",File.ReadAllText(path),StringComparison.Ordinal);}
    }
    private static string RepositoryRoot(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"HRSystem.slnx")))d=d.Parent;return d?.FullName??throw new InvalidOperationException("Repository root not found.");}
    private sealed class StubService:IAttendanceExceptionService
    {
        public int MyRequestsCallCount { get; private set; }
        public Task<IReadOnlyList<AttendanceExceptionEmployeeOptionDto>> GetEmployeeOptionsAsync(CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<AttendanceExceptionEmployeeOptionDto>>([new(Guid.NewGuid(),"EMP9101","測試員工")]);
        public Task<AttendanceExceptionReviewContextDto> GetReviewContextAsync(Guid id,CancellationToken ct=default)=>Task.FromResult(new AttendanceExceptionReviewContextDto("正常班 08:00～12:00、13:30～17:30",0,"無原始打卡","保留班表必須出勤分鐘，豁免全日出勤異常"));
        public Task<PagedResult<AttendanceExceptionDto>> GetMyRequestsAsync(AttendanceExceptionQuery q,CancellationToken ct=default){MyRequestsCallCount++;return Task.FromResult(new PagedResult<AttendanceExceptionDto>([Dto()],1,1,20));}
        public Task<PagedResult<AttendanceExceptionDto>> GetPendingApprovalsAsync(AttendanceExceptionQuery q,CancellationToken ct=default)=>Task.FromResult(new PagedResult<AttendanceExceptionDto>([Dto()],1,1,20));
        public Task<AttendanceExceptionDto> GetDetailAsync(Guid id,CancellationToken ct=default)=>Task.FromResult(Dto());
        public Task<AttendanceExceptionDto> CreateDraftAsync(CreateAttendanceExceptionDraftRequest r,CancellationToken ct=default)=>Task.FromResult(Dto());
        public Task<AttendanceExceptionDto> UpdateDraftAsync(UpdateAttendanceExceptionDraftRequest r,CancellationToken ct=default)=>Task.FromResult(Dto());
        public Task<AttendanceExceptionDto> SubmitAsync(AttendanceExceptionActionRequest r,CancellationToken ct=default)=>Task.FromResult(Dto());public Task<AttendanceExceptionDto> WithdrawAsync(AttendanceExceptionActionRequest r,CancellationToken ct=default)=>Task.FromResult(Dto());public Task<AttendanceExceptionDto> ApproveAsync(AttendanceExceptionActionRequest r,CancellationToken ct=default)=>Task.FromResult(Dto());public Task<AttendanceExceptionDto> RejectAsync(AttendanceExceptionReasonActionRequest r,CancellationToken ct=default)=>Task.FromResult(Dto());public Task<AttendanceExceptionDto> RequestCancellationAsync(AttendanceExceptionReasonActionRequest r,CancellationToken ct=default)=>Task.FromResult(Dto());public Task<AttendanceExceptionDto> ApproveCancellationAsync(AttendanceExceptionActionRequest r,CancellationToken ct=default)=>Task.FromResult(Dto());public Task<AttendanceExceptionDto> RejectCancellationAsync(AttendanceExceptionReasonActionRequest r,CancellationToken ct=default)=>Task.FromResult(Dto());
        private static AttendanceExceptionDto Dto()=>new(Guid.NewGuid(),"AE-TEST",Guid.NewGuid(),"EMP9101","測試員工",Guid.NewGuid(),"測試部",new DateOnly(2026,8,12),AttendanceExceptionType.NaturalDisaster,NaturalDisasterReasonType.WorkplaceClosure,AttendanceExceptionImpactType.FullDay,null,null,"颱風",AttendanceExceptionStatus.Submitted,null,DateTimeOffset.UtcNow,string.Empty,[]);
    }
}
