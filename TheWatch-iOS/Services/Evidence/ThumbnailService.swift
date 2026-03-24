// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         ThumbnailService.swift
// Purpose:      Protocol + mock for generating evidence thumbnails.
//               Photos: 200x200 center-crop. Video: first frame extraction.
//               Audio: waveform image generation.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, UIKit (CGImage), AVFoundation
//
// Usage Example:
//   let service: ThumbnailServiceProtocol = MockThumbnailService()
//   let thumbURL = try await service.generateThumbnail(for: evidence)
//
// Potential Additions:
//   - GPU-accelerated thumbnail generation via Metal
//   - Smart crop using Vision framework face detection
//   - Animated GIF thumbnails for video
// ============================================================================

import Foundation

// MARK: - Protocol

protocol ThumbnailServiceProtocol: AnyObject {
    /// Generate a 200x200 thumbnail for the given evidence item.
    /// - Parameter evidence: The evidence item to thumbnail
    /// - Returns: URL to the generated thumbnail file
    func generateThumbnail(for evidence: Evidence) async throws -> URL

    /// Generate thumbnails for multiple evidence items in batch.
    /// - Parameter items: Array of evidence items
    /// - Returns: Dictionary mapping evidence ID to thumbnail URL
    func generateThumbnails(for items: [Evidence]) async throws -> [UUID: URL]

    /// Clear cached thumbnails older than the given interval.
    /// - Parameter olderThan: Time interval threshold
    func clearCache(olderThan: TimeInterval) async throws
}

// MARK: - Mock Implementation

@Observable
final class MockThumbnailService: ThumbnailServiceProtocol {

    private var cache: [UUID: URL] = [:]

    func generateThumbnail(for evidence: Evidence) async throws -> URL {
        // Simulate processing time based on type
        let delay: UInt64 = switch evidence.type {
        case .photo: 50_000_000    // 50ms
        case .video: 200_000_000   // 200ms - first frame extraction
        case .audio: 150_000_000   // 150ms - waveform generation
        case .sitrep: 20_000_000   // 20ms - text preview
        }
        try await Task.sleep(nanoseconds: delay)

        let suffix = switch evidence.type {
        case .photo: "thumb_photo"
        case .video: "thumb_frame"
        case .audio: "waveform"
        case .sitrep: "sitrep_preview"
        }

        let url = URL(fileURLWithPath: "/mock/thumbnails/\(suffix)_\(evidence.id.uuidString.prefix(8)).png")
        cache[evidence.id] = url
        return url
    }

    func generateThumbnails(for items: [Evidence]) async throws -> [UUID: URL] {
        var results: [UUID: URL] = [:]
        for item in items {
            results[item.id] = try await generateThumbnail(for: item)
        }
        return results
    }

    func clearCache(olderThan: TimeInterval) async throws {
        // Mock: clear everything
        cache.removeAll()
    }
}
