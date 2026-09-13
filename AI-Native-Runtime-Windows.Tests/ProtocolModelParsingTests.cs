using System.Text.Json;
using AI_Native_Runtime_Windows.Models;
using Xunit;

namespace AI_Native_Runtime_Windows.Tests
{
    /// <summary>Parses literal JSON shaped exactly like CORE's real wire output (per
    /// `docs/local-api.md` and `desktop-shell-conventions.md` §1), not invented shapes -
    /// the same discipline the plan requires of the client models themselves.</summary>
    public class ProtocolModelParsingTests
    {
        [Fact]
        public void ExecutionItem_tolerates_absent_correlationId_and_requestedByUserId()
        {
            // §1: a `#[serde(skip_serializing_if = "Option::is_none")]` field is
            // omitted entirely for a local execution, never sent as `null`.
            var json = """
            {
                "id": "exec_1",
                "capabilityId": "filesystem.read@1",
                "origin": "LOCAL",
                "state": "COMPLETED",
                "requestedByApplicationId": "app_desktop_windows",
                "createdAt": "2026-09-13T00:00:00Z"
            }
            """;
            var element = JsonDocument.Parse(json).RootElement;
            var item = ExecutionItem.FromJson(element);

            Assert.Equal("exec_1", item.Id);
            Assert.Null(item.CorrelationId);
            Assert.Null(item.RequestedByUserId);
            Assert.False(item.IsCloudDispatched);
            Assert.False(item.HasCorrelationId);
        }

        [Fact]
        public void ExecutionItem_marks_cloud_dispatched_origin_and_keeps_correlation_id()
        {
            var json = """
            {
                "id": "exec_2",
                "capabilityId": "filesystem.read@1",
                "origin": "CLOUD_DISPATCHED",
                "state": "COMPLETED",
                "requestedByApplicationId": "rtapi_orchestrator",
                "requestedByUserId": "user_1",
                "correlationId": "corr_1",
                "createdAt": "2026-09-13T00:00:00Z"
            }
            """;
            var item = ExecutionItem.FromJson(JsonDocument.Parse(json).RootElement);

            Assert.True(item.IsCloudDispatched);
            Assert.Equal("user_1", item.RequestedByUserId);
            Assert.Equal("corr_1", item.CorrelationId);
            Assert.True(item.HasCorrelationId);
        }

        [Fact]
        public void ApprovalItem_builds_distinguishable_automation_labels_per_row()
        {
            var json = """
            {
                "id": "apr_1",
                "executionId": "exec_1",
                "applicationId": "app_desktop_windows",
                "capabilityId": "filesystem.read@1",
                "resource": "/etc/passwd",
                "baselineRisk": "LOW",
                "effectiveRisk": "HIGH",
                "state": "PENDING"
            }
            """;
            var item = ApprovalItem.FromJson(JsonDocument.Parse(json).RootElement);

            Assert.Contains("filesystem.read@1", item.DenyAutomationLabel);
            Assert.Contains("/etc/passwd", item.DenyAutomationLabel);
            Assert.Contains("filesystem.read@1", item.AllowAutomationLabel);
            Assert.Contains("HIGH", item.AccessibilitySummary);
        }

        [Fact]
        public void CapabilityItem_reads_the_nested_definition_capabilityId()
        {
            var json = """
            {
                "definition": { "capabilityId": "python.run@1" },
                "availability": "AVAILABLE",
                "providerKind": "WORKER",
                "providerWorkerId": "worker_1"
            }
            """;
            var item = CapabilityItem.FromJson(JsonDocument.Parse(json).RootElement);

            Assert.Equal("python.run@1", item.CapabilityId);
            Assert.Equal("WORKER", item.ProviderKind);
            Assert.Equal("worker_1", item.ProviderWorkerId);
        }
    }
}
