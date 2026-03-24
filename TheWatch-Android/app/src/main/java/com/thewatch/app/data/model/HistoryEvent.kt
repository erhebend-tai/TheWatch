package com.thewatch.app.data.model

import java.time.LocalDateTime

data class HistoryEvent(
    val id: String = "",
    val userId: String = "",
    val eventType: String = "",
    val severity: String = "",
    val timestamp: LocalDateTime = LocalDateTime.now(),
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val location: String = "",
    val description: String = "",
    val responderName: String = "",
    val responderId: String = "",
    val status: String = "",
    val resolution: String = ""
)
