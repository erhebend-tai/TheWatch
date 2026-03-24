package com.thewatch.app.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.YellowWarning

@Composable
fun PasswordStrengthMeter(password: String) {
    val strength = calculatePasswordStrength(password)
    val strengthColor = when (strength) {
        PasswordStrength.WEAK -> RedPrimary
        PasswordStrength.FAIR -> YellowWarning
        PasswordStrength.GOOD -> Color(0xFF4CAF50)
        PasswordStrength.STRONG -> GreenSafe
    }

    val strengthText = when (strength) {
        PasswordStrength.WEAK -> "Weak"
        PasswordStrength.FAIR -> "Fair"
        PasswordStrength.GOOD -> "Good"
        PasswordStrength.STRONG -> "Strong"
    }

    val filledBars = when (strength) {
        PasswordStrength.WEAK -> 1
        PasswordStrength.FAIR -> 2
        PasswordStrength.GOOD -> 3
        PasswordStrength.STRONG -> 4
    }

    Column {
        Row(modifier = Modifier.fillMaxWidth()) {
            repeat(4) { index ->
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .height(4.dp)
                        .background(
                            if (index < filledBars) strengthColor else Color.LightGray
                        )
                )
                if (index < 3) {
                    Box(modifier = Modifier.weight(0.1f))
                }
            }
        }
        Text(
            text = "Password Strength: $strengthText",
            fontSize = 12.sp,
            fontWeight = FontWeight.Medium,
            color = strengthColor,
            modifier = Modifier.weight(1f)
        )
    }
}

enum class PasswordStrength {
    WEAK, FAIR, GOOD, STRONG
}

fun calculatePasswordStrength(password: String): PasswordStrength {
    if (password.isEmpty()) return PasswordStrength.WEAK

    var strength = 0
    if (password.length >= 8) strength++
    if (password.length >= 12) strength++
    if (password.any { it.isUpperCase() }) strength++
    if (password.any { it.isDigit() }) strength++
    if (password.any { !it.isLetterOrDigit() }) strength++

    return when {
        strength <= 1 -> PasswordStrength.WEAK
        strength == 2 -> PasswordStrength.FAIR
        strength == 3 -> PasswordStrength.GOOD
        else -> PasswordStrength.STRONG
    }
}
