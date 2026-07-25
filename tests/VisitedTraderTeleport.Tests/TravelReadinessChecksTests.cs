using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TravelReadinessChecksTests
{
    [Fact]
    public void IsMeshQueueSaturated_BelowThreshold_ReturnsFalse()
    {
        Assert.False(TravelReadinessChecks.IsMeshQueueSaturated(queuedCount: 39, maxQueuedMeshLayers: 50));
    }

    [Fact]
    public void IsMeshQueueSaturated_ExactlyAtThreshold_ReturnsTrue()
    {
        // 50 * 0.8 = 40
        Assert.True(TravelReadinessChecks.IsMeshQueueSaturated(queuedCount: 40, maxQueuedMeshLayers: 50));
    }

    [Fact]
    public void IsMeshQueueSaturated_AboveThreshold_ReturnsTrue()
    {
        Assert.True(TravelReadinessChecks.IsMeshQueueSaturated(queuedCount: 45, maxQueuedMeshLayers: 50));
    }

    [Fact]
    public void IsDestinationReady_ChunkNotLoaded_ReturnsFalse()
    {
        bool result = TravelReadinessChecks.IsDestinationReady(
            isChunkAreaLoaded: false, isDedicatedServer: true, requireColliders: false, isCollidersLoaded: true);

        Assert.False(result);
    }

    [Fact]
    public void IsDestinationReady_DedicatedServer_IgnoresColliders()
    {
        bool result = TravelReadinessChecks.IsDestinationReady(
            isChunkAreaLoaded: true, isDedicatedServer: true, requireColliders: true, isCollidersLoaded: false);

        Assert.True(result);
    }

    [Fact]
    public void IsDestinationReady_CollidersNotRequired_ReturnsTrue()
    {
        bool result = TravelReadinessChecks.IsDestinationReady(
            isChunkAreaLoaded: true, isDedicatedServer: false, requireColliders: false, isCollidersLoaded: false);

        Assert.True(result);
    }

    [Fact]
    public void IsDestinationReady_CollidersRequiredAndNotLoaded_ReturnsFalse()
    {
        bool result = TravelReadinessChecks.IsDestinationReady(
            isChunkAreaLoaded: true, isDedicatedServer: false, requireColliders: true, isCollidersLoaded: false);

        Assert.False(result);
    }

    [Fact]
    public void IsDestinationReady_CollidersRequiredAndLoaded_ReturnsTrue()
    {
        bool result = TravelReadinessChecks.IsDestinationReady(
            isChunkAreaLoaded: true, isDedicatedServer: false, requireColliders: true, isCollidersLoaded: true);

        Assert.True(result);
    }
}
