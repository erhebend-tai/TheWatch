// WAL: Tests for ResponseScopePresets.GetDefaults() — verifies that every ResponseScope
// enum value returns the correct preset configuration (radius, responder count, escalation
// policy, dispatch strategy, escalation timeout). These presets are life-safety critical:
// a wrong default could dispatch too few responders or fail to escalate to 911.
//
// Example:
//   var (radius, responders, escalation, strategy, timeout) = ResponseScopePresets.GetDefaults(ResponseScope.CheckIn);
//   Assert.Equal(1000, radius);
//   Assert.Equal(8, responders);

using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Shared.Tests;

public class ResponseScopePresetsTests
{
    // ═══════════════════════════════════════════════════════════════
    // CheckIn scope — small radius, few responders, no auto-escalation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetDefaults_CheckIn_ReturnsCorrectRadius()
    {
        var (radius, _, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.CheckIn);
        Assert.Equal(1000, radius);
    }

    [Fact]
    public void GetDefaults_CheckIn_ReturnsCorrectDesiredResponders()
    {
        var (_, responders, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.CheckIn);
        Assert.Equal(8, responders);
    }

    [Fact]
    public void GetDefaults_CheckIn_ReturnsManualEscalation()
    {
        var (_, _, escalation, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.CheckIn);
        Assert.Equal(EscalationPolicy.Manual, escalation);
    }

    [Fact]
    public void GetDefaults_CheckIn_ReturnsNearestNStrategy()
    {
        var (_, _, _, strategy, _) = ResponseScopePresets.GetDefaults(ResponseScope.CheckIn);
        Assert.Equal(DispatchStrategy.NearestN, strategy);
    }

    [Fact]
    public void GetDefaults_CheckIn_ReturnsFiveMinuteTimeout()
    {
        var (_, _, _, _, timeout) = ResponseScopePresets.GetDefaults(ResponseScope.CheckIn);
        Assert.Equal(TimeSpan.FromMinutes(5), timeout);
    }

    // ═══════════════════════════════════════════════════════════════
    // Neighborhood scope — medium radius, timed escalation, certified first
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetDefaults_Neighborhood_ReturnsCorrectRadius()
    {
        var (radius, _, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Neighborhood);
        Assert.Equal(3000, radius);
    }

    [Fact]
    public void GetDefaults_Neighborhood_ReturnsCorrectDesiredResponders()
    {
        var (_, responders, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Neighborhood);
        Assert.Equal(15, responders);
    }

    [Fact]
    public void GetDefaults_Neighborhood_ReturnsTimedEscalation()
    {
        var (_, _, escalation, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Neighborhood);
        Assert.Equal(EscalationPolicy.TimedEscalation, escalation);
    }

    [Fact]
    public void GetDefaults_Neighborhood_ReturnsCertifiedFirstStrategy()
    {
        var (_, _, _, strategy, _) = ResponseScopePresets.GetDefaults(ResponseScope.Neighborhood);
        Assert.Equal(DispatchStrategy.CertifiedFirst, strategy);
    }

    [Fact]
    public void GetDefaults_Neighborhood_ReturnsTwoMinuteTimeout()
    {
        var (_, _, _, _, timeout) = ResponseScopePresets.GetDefaults(ResponseScope.Neighborhood);
        Assert.Equal(TimeSpan.FromMinutes(2), timeout);
    }

    // ═══════════════════════════════════════════════════════════════
    // Community scope — large radius, immediate 911, broadcast
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetDefaults_Community_ReturnsCorrectRadius()
    {
        var (radius, _, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Community);
        Assert.Equal(10000, radius);
    }

    [Fact]
    public void GetDefaults_Community_ReturnsCorrectDesiredResponders()
    {
        var (_, responders, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Community);
        Assert.Equal(50, responders);
    }

    [Fact]
    public void GetDefaults_Community_ReturnsImmediate911Escalation()
    {
        var (_, _, escalation, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Community);
        Assert.Equal(EscalationPolicy.Immediate911, escalation);
    }

    [Fact]
    public void GetDefaults_Community_ReturnsRadiusBroadcastStrategy()
    {
        var (_, _, _, strategy, _) = ResponseScopePresets.GetDefaults(ResponseScope.Community);
        Assert.Equal(DispatchStrategy.RadiusBroadcast, strategy);
    }

    [Fact]
    public void GetDefaults_Community_ReturnsOneMinuteTimeout()
    {
        var (_, _, _, _, timeout) = ResponseScopePresets.GetDefaults(ResponseScope.Community);
        Assert.Equal(TimeSpan.FromMinutes(1), timeout);
    }

    // ═══════════════════════════════════════════════════════════════
    // Evacuation scope — unlimited radius/responders, full cascade, zero timeout
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetDefaults_Evacuation_ReturnsCorrectRadius()
    {
        var (radius, _, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Evacuation);
        Assert.Equal(50000, radius);
    }

    [Fact]
    public void GetDefaults_Evacuation_ReturnsIntMaxValueResponders()
    {
        var (_, responders, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Evacuation);
        Assert.Equal(int.MaxValue, responders);
    }

    [Fact]
    public void GetDefaults_Evacuation_ReturnsFullCascadeEscalation()
    {
        var (_, _, escalation, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Evacuation);
        Assert.Equal(EscalationPolicy.FullCascade, escalation);
    }

    [Fact]
    public void GetDefaults_Evacuation_ReturnsEmergencyBroadcastStrategy()
    {
        var (_, _, _, strategy, _) = ResponseScopePresets.GetDefaults(ResponseScope.Evacuation);
        Assert.Equal(DispatchStrategy.EmergencyBroadcast, strategy);
    }

    [Fact]
    public void GetDefaults_Evacuation_ReturnsZeroTimeout()
    {
        var (_, _, _, _, timeout) = ResponseScopePresets.GetDefaults(ResponseScope.Evacuation);
        Assert.Equal(TimeSpan.Zero, timeout);
    }

    // ═══════════════════════════════════════════════════════════════
    // SilentDuress scope — smallest radius, trusted contacts only
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetDefaults_SilentDuress_ReturnsCorrectRadius()
    {
        var (radius, _, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.SilentDuress);
        Assert.Equal(500, radius);
    }

    [Fact]
    public void GetDefaults_SilentDuress_ReturnsThreeResponders()
    {
        var (_, responders, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.SilentDuress);
        Assert.Equal(3, responders);
    }

    [Fact]
    public void GetDefaults_SilentDuress_ReturnsConditional911Escalation()
    {
        var (_, _, escalation, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.SilentDuress);
        Assert.Equal(EscalationPolicy.Conditional911, escalation);
    }

    [Fact]
    public void GetDefaults_SilentDuress_ReturnsTrustedContactsOnlyStrategy()
    {
        var (_, _, _, strategy, _) = ResponseScopePresets.GetDefaults(ResponseScope.SilentDuress);
        Assert.Equal(DispatchStrategy.TrustedContactsOnly, strategy);
    }

    [Fact]
    public void GetDefaults_SilentDuress_ReturnsThreeMinuteTimeout()
    {
        var (_, _, _, _, timeout) = ResponseScopePresets.GetDefaults(ResponseScope.SilentDuress);
        Assert.Equal(TimeSpan.FromMinutes(3), timeout);
    }

    // ═══════════════════════════════════════════════════════════════
    // Custom scope — balanced defaults for user-defined scopes
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetDefaults_Custom_ReturnsCorrectRadius()
    {
        var (radius, _, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Custom);
        Assert.Equal(2000, radius);
    }

    [Fact]
    public void GetDefaults_Custom_ReturnsTenResponders()
    {
        var (_, responders, _, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Custom);
        Assert.Equal(10, responders);
    }

    [Fact]
    public void GetDefaults_Custom_ReturnsTimedEscalation()
    {
        var (_, _, escalation, _, _) = ResponseScopePresets.GetDefaults(ResponseScope.Custom);
        Assert.Equal(EscalationPolicy.TimedEscalation, escalation);
    }

    [Fact]
    public void GetDefaults_Custom_ReturnsNearestNStrategy()
    {
        var (_, _, _, strategy, _) = ResponseScopePresets.GetDefaults(ResponseScope.Custom);
        Assert.Equal(DispatchStrategy.NearestN, strategy);
    }

    [Fact]
    public void GetDefaults_Custom_ReturnsTwoMinuteTimeout()
    {
        var (_, _, _, _, timeout) = ResponseScopePresets.GetDefaults(ResponseScope.Custom);
        Assert.Equal(TimeSpan.FromMinutes(2), timeout);
    }

    // ═══════════════════════════════════════════════════════════════
    // Invalid enum value — must throw ArgumentOutOfRangeException
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetDefaults_InvalidScope_ThrowsArgumentOutOfRangeException()
    {
        var invalidScope = (ResponseScope)999;
        Assert.Throws<ArgumentOutOfRangeException>(() => ResponseScopePresets.GetDefaults(invalidScope));
    }

    // ═══════════════════════════════════════════════════════════════
    // Theory-based comprehensive validation — all scopes in one pass
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(ResponseScope.CheckIn, 1000, 8)]
    [InlineData(ResponseScope.Neighborhood, 3000, 15)]
    [InlineData(ResponseScope.Community, 10000, 50)]
    [InlineData(ResponseScope.Evacuation, 50000, int.MaxValue)]
    [InlineData(ResponseScope.SilentDuress, 500, 3)]
    [InlineData(ResponseScope.Custom, 2000, 10)]
    public void GetDefaults_AllScopes_ReturnCorrectRadiusAndResponders(
        ResponseScope scope, double expectedRadius, int expectedResponders)
    {
        var (radius, responders, _, _, _) = ResponseScopePresets.GetDefaults(scope);
        Assert.Equal(expectedRadius, radius);
        Assert.Equal(expectedResponders, responders);
    }

    [Theory]
    [InlineData(ResponseScope.CheckIn, EscalationPolicy.Manual)]
    [InlineData(ResponseScope.Neighborhood, EscalationPolicy.TimedEscalation)]
    [InlineData(ResponseScope.Community, EscalationPolicy.Immediate911)]
    [InlineData(ResponseScope.Evacuation, EscalationPolicy.FullCascade)]
    [InlineData(ResponseScope.SilentDuress, EscalationPolicy.Conditional911)]
    [InlineData(ResponseScope.Custom, EscalationPolicy.TimedEscalation)]
    public void GetDefaults_AllScopes_ReturnCorrectEscalationPolicy(
        ResponseScope scope, EscalationPolicy expectedPolicy)
    {
        var (_, _, escalation, _, _) = ResponseScopePresets.GetDefaults(scope);
        Assert.Equal(expectedPolicy, escalation);
    }

    [Theory]
    [InlineData(ResponseScope.CheckIn, DispatchStrategy.NearestN)]
    [InlineData(ResponseScope.Neighborhood, DispatchStrategy.CertifiedFirst)]
    [InlineData(ResponseScope.Community, DispatchStrategy.RadiusBroadcast)]
    [InlineData(ResponseScope.Evacuation, DispatchStrategy.EmergencyBroadcast)]
    [InlineData(ResponseScope.SilentDuress, DispatchStrategy.TrustedContactsOnly)]
    [InlineData(ResponseScope.Custom, DispatchStrategy.NearestN)]
    public void GetDefaults_AllScopes_ReturnCorrectDispatchStrategy(
        ResponseScope scope, DispatchStrategy expectedStrategy)
    {
        var (_, _, _, strategy, _) = ResponseScopePresets.GetDefaults(scope);
        Assert.Equal(expectedStrategy, strategy);
    }
}
