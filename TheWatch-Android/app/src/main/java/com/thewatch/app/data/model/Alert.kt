package com.thewatch.app.data.model

import java.time.LocalDateTime

data class Alert(
    val id: String = "",
    val userId: String = "",
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val description: String = "",
    val timestamp: LocalDateTime = LocalDateTime.now(),
    val severity: String = "HIGH",
    val status: String = "ACTIVE",
    val respondersCount: Int = 0
)
