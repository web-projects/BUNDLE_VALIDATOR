using Devices.Core.State.Enums;

using static Devices.Core.State.Enums.DeviceSubWorkflowState;

namespace Devices.Core.State.SubWorkflows
{
    public static class DeviceSubStateTransitionHelper
    {
        private static DeviceSubWorkflowState ComputeDisplayIdleScreenStateTransition(bool exception) =>
        exception switch
        {
            true => SanityCheck,
            false => SanityCheck
        };

        private static DeviceSubWorkflowState ComputeEnableADKLoggerStateTransition(bool exception) =>
        exception switch
        {
            true => SanityCheck,
            false => SanityCheck
        };

        private static DeviceSubWorkflowState ComputeADKLoggerResetStateTransition(bool exception) =>
        exception switch
        {
            true => SanityCheck,
            false => SanityCheck
        };

        private static DeviceSubWorkflowState ComputeGetTerminalLogsStateTransition(bool exception) =>
        exception switch
        {
            true => SanityCheck,
            false => SanityCheck
        };

        private static DeviceSubWorkflowState ComputeReportVipaVersionsStateTransition(bool exception) =>
        exception switch
        {
            true => SanityCheck,
            false => SanityCheck
        };

        private static DeviceSubWorkflowState ComputeReportBundleVersionsStateTransition(bool exception) =>
        exception switch
        {
            true => SanityCheck,
            false => SanityCheck
        };

        private static DeviceSubWorkflowState ComputeSanityCheckStateTransition(bool exception) =>
            exception switch
            {
                true => RequestComplete,
                false => RequestComplete
            };

        private static DeviceSubWorkflowState ComputeRequestCompletedStateTransition(bool exception) =>
            exception switch
            {
                true => Undefined,
                false => Undefined
            };

        public static DeviceSubWorkflowState GetNextState(DeviceSubWorkflowState state, bool exception) =>
            state switch
            {
                DisplayIdleScreen => ComputeDisplayIdleScreenStateTransition(exception),
                EnableADKLogger => ComputeEnableADKLoggerStateTransition(exception),
                ADKLoggerReset => ComputeADKLoggerResetStateTransition(exception),
                GetTerminalLogs => ComputeGetTerminalLogsStateTransition(exception),
                ReportVIPAVersions => ComputeReportVipaVersionsStateTransition(exception),
                ReportBundleVersions => ComputeReportVipaVersionsStateTransition(exception),
                SanityCheck => ComputeSanityCheckStateTransition(exception),
                RequestComplete => ComputeRequestCompletedStateTransition(exception),
                _ => throw new StateException($"Invalid state transition '{state}' requested.")
            };
    }
}
