namespace VisitedTraderTeleport;

internal static class TravelReadinessChecks
{
    private const float MeshQueueBusyFraction = 0.8f;

    // Mirrors the game's own throttle in ChunkManager.thread_Regenerating: when the number of
    // in-use VoxelMeshLayers reaches MaxQueuedMeshLayers, the mesh regeneration thread blocks.
    // Starting another map-wide trip in that state piles more work onto an already choking
    // queue, so refuse (without charging) while it is close to the limit.
    public static bool IsMeshQueueSaturated(int queuedCount, int maxQueuedMeshLayers)
    {
        return queuedCount >= maxQueuedMeshLayers * MeshQueueBusyFraction;
    }

    public static bool IsDestinationReady(bool isChunkAreaLoaded, bool isDedicatedServer, bool requireColliders, bool isCollidersLoaded)
    {
        if (!isChunkAreaLoaded)
        {
            return false;
        }

        return isDedicatedServer || !requireColliders || isCollidersLoaded;
    }
}
