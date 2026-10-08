namespace HRSystem.Application.Overtime;

public interface IOvertimeRecognitionService
{
    Task<IReadOnlyList<OvertimeRecognitionDto>> SearchAsync(
        OvertimeRecognitionQuery query,
        CancellationToken cancellationToken = default);
    Task<OvertimeRecognitionDto> ConfirmAsync(
        ConfirmOvertimeRecognitionRequest request,
        CancellationToken cancellationToken = default);
    Task<OvertimeRecognitionDto> ReopenAsync(
        ReopenOvertimeRecognitionRequest request,
        CancellationToken cancellationToken = default);
}
