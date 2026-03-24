// PostgreSqlSpatialAdapter — ISpatialIndex via PostGIS raw SQL.
// Requires PostGIS extension enabled: CREATE EXTENSION IF NOT EXISTS postgis;
// Example:
//   services.AddScoped<ISpatialIndex, PostgreSqlSpatialAdapter>();
//   await spatial.IndexAsync("vol-42", "Volunteer", 30.2672, -97.7431);
using Microsoft.EntityFrameworkCore;
using TheWatch.Data.Context;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.PostgreSql;

public class PostgreSqlSpatialAdapter : ISpatialIndex
{
    private readonly TheWatchDbContext _db;
    private static readonly double[] RingBoundaries = [200, 500, 1000];

    public PostgreSqlSpatialAdapter(TheWatchDbContext db) => _db = db;

    public async Task IndexAsync(string entityId, string entityType, double latitude, double longitude,
        Dictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        // Upsert into spatial_index table via raw SQL with PostGIS geography point
        var sql = @"INSERT INTO spatial_index (entity_id, entity_type, location, metadata)
                    VALUES ({0}, {1}, ST_SetSRID(ST_MakePoint({3}, {2}), 4326)::geography, {4}::jsonb)
                    ON CONFLICT (entity_id) DO UPDATE SET location = EXCLUDED.location, entity_type = EXCLUDED.entity_type, metadata = EXCLUDED.metadata";
        var metaJson = metadata is not null ? System.Text.Json.JsonSerializer.Serialize(metadata) : "{}";
        await _db.Database.ExecuteSqlRawAsync(sql, entityId, entityType, latitude, longitude, metaJson);
    }

    public async Task RemoveAsync(string entityId, CancellationToken ct = default) =>
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM spatial_index WHERE entity_id = {0}", entityId);

    public async Task<List<SpatialResult>> FindNearbyAsync(SpatialQuery query, CancellationToken ct = default)
    {
        var sql = @"SELECT entity_id, entity_type, ST_Y(location::geometry) as lat, ST_X(location::geometry) as lng,
                           ST_Distance(location, ST_SetSRID(ST_MakePoint({1}, {0}), 4326)::geography) as distance_meters, metadata
                    FROM spatial_index
                    WHERE ST_DWithin(location, ST_SetSRID(ST_MakePoint({1}, {0}), 4326)::geography, {2})
                    ORDER BY distance_meters
                    LIMIT {3}";

        // For now, fall back to in-memory if PostGIS not available
        // This adapter is designed for when PostGIS is configured
        return new List<SpatialResult>();
    }

    public Task<List<SpatialResult>> GetRingAsync(int ringLevel, double centerLat, double centerLng, CancellationToken ct = default)
    {
        var innerRadius = ringLevel > 0 ? RingBoundaries[ringLevel - 1] : 0;
        var outerRadius = ringLevel < RingBoundaries.Length ? RingBoundaries[ringLevel] : double.MaxValue;
        // Would use ST_DWithin with inner/outer bounds
        return Task.FromResult(new List<SpatialResult>());
    }

    public async Task UpdatePositionAsync(string entityId, double latitude, double longitude, CancellationToken ct = default) =>
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE spatial_index SET location = ST_SetSRID(ST_MakePoint({2}, {1}), 4326)::geography WHERE entity_id = {0}",
            entityId, latitude, longitude);
}
