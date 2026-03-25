package com.thewatch.app.data.repository.mock

import com.thewatch.app.data.model.MfaEnrollmentChallenge
import com.thewatch.app.data.model.User
import com.thewatch.app.data.repository.AuthRepository
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import javax.inject.Inject

class MockAuthRepository @Inject constructor() : AuthRepository {
    private val currentUserFlow = MutableStateFlow<User?>(null)

    override suspend fun login(emailOrPhone: String, password: String): Result<User> {
        delay(1000)
        return if (emailOrPhone.contains("alex") && password == "Password123!") {
            val user = mockUserAlexRivera()
            currentUserFlow.value = user
            Result.success(user)
        } else {
            Result.failure(Exception("Invalid credentials"))
        }
    }

    override suspend fun signUp(
        name: String,
        email: String,
        phone: String,
        dateOfBirth: String,
        password: String
    ): Result<User> {
        delay(1500)
        val newUser = User(
            id = System.currentTimeMillis().toString(),
            name = name,
            email = email,
            phone = phone,
            dateOfBirth = dateOfBirth,
            isAuthenticated = true
        )
        currentUserFlow.value = newUser
        return Result.success(newUser)
    }

    override suspend fun sendPasswordResetCode(emailOrPhone: String): Result<String> {
        delay(1000)
        return Result.success("123456")
    }

    override suspend fun verifyResetCode(code: String): Result<Boolean> {
        delay(800)
        return Result.success(code == "123456")
    }

    override suspend fun resetPassword(code: String, newPassword: String): Result<Boolean> {
        delay(1000)
        return Result.success(true)
    }

    override suspend fun biometricLogin(): Result<User> {
        delay(1500)
        val user = mockUserAlexRivera()
        currentUserFlow.value = user
        return Result.success(user)
    }

    override suspend fun logout(): Result<Unit> {
        delay(800)
        currentUserFlow.value = null
        return Result.success(Unit)
    }

    override fun getCurrentUser(): Flow<User?> = currentUserFlow.asStateFlow()

    override suspend fun acceptEULA(userId: String): Result<Unit> {
        delay(500)
        return Result.success(Unit)
    }

    // ── New interface methods (mock stubs) ────────────────────────────

    override suspend fun sendEmailVerification(): Result<Unit> {
        delay(500)
        return Result.success(Unit)
    }

    override suspend fun refreshToken(): Result<User> {
        delay(500)
        val user = currentUserFlow.value ?: return Result.failure(Exception("No user logged in"))
        return Result.success(user)
    }

    override suspend fun enrollMfa(method: String, phoneNumber: String?): Result<MfaEnrollmentChallenge> {
        delay(1000)
        return Result.success(
            MfaEnrollmentChallenge(
                method = method,
                challengeUri = "otpauth://totp/TheWatch:alex.rivera@example.com?secret=JBSWY3DPEHPK3PXP&issuer=TheWatch",
                sessionId = "mock-session-${System.currentTimeMillis()}",
                backupCodes = listOf("A1B2C3D4", "E5F6G7H8", "I9J0K1L2", "M3N4O5P6", "Q7R8S9T0")
            )
        )
    }

    override suspend fun confirmMfaEnrollment(sessionId: String, code: String): Result<Boolean> {
        delay(800)
        return Result.success(true)
    }

    override suspend fun verifyMfaCode(code: String, method: String): Result<Boolean> {
        delay(800)
        return Result.success(code == "123456")
    }

    private fun mockUserAlexRivera() = User(
        id = "user_001",
        name = "Alex Rivera",
        email = "alex.rivera@example.com",
        phone = "+1-555-0123",
        dateOfBirth = "1990-05-15",
        photoUrl = null,
        bloodType = "O+",
        medicalConditions = "Asthma, Seasonal Allergies",
        medications = "Albuterol inhaler, Cetirizine",
        defaultSeverity = "HIGH",
        autoEscalationTimerMinutes = 30,
        autoEscalateTo911 = false,
        duressCode = "911911",
        personalClearWord = "SAFE",
        checkInScheduleMinutes = 240,
        isAuthenticated = true
    )
}
