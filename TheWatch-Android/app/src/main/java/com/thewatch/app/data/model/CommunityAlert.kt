package com.thewatch.app.data.model

import java.time.LocalDateTime

data class CommunityAlert(
    val id: String = "",
    val type: String = "",
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val description: String = "",
    val timestamp: LocalDateTime = LocalDateTime.now(),
    val severity: String = "Medium",
    val respondersCount: Int = 0
)
